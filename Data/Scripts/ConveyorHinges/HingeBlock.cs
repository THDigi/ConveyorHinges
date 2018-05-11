using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

//using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;
//#pragma warning disable CS0162 // Unreachable code detected

namespace Digi.ConveyorHinges
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_MotorAdvancedStator), false, ConveyorHingesMod.SMALL_STATOR, ConveyorHingesMod.MEDIUM_STATOR, ConveyorHingesMod.LARGE_STATOR)]
    public class HingeBlock : MyGameLogicComponent
    {
        private IMyMotorAdvancedStator block;
        private float limitRad;
        private double maxViewDistSq;
        private float blockLength;
        private float attachRadius;
        private int attachRequestCooldown = 0;
        private bool isPlayer = false;
        private MyEntitySubpart[] subparts;
        private MyEntitySubpart subpartEnd;

        private int lastModelId;
        private bool hasMainModel = false;
        private bool refreshSubparts = true;
        private bool visible = false;

        private Vector3 curveStart;
        private Vector3 curveHandles = Vector3.Zero;

        private Vector3 ragdollPositionLocal;
        private Vector3 ragdollVelocityLocal;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            try
            {
                ConveyorHingesMod.SetupControls<IMyMotorAdvancedStator>(); // this sets up only once per world

                block = (IMyMotorAdvancedStator)Entity;

                ConveyorHingesMod.HingeData data;

                if(block.CubeGrid.Physics != null && ConveyorHingesMod.Instance.Hinges.TryGetValue(block.SlimBlock.BlockDefinition.Id.SubtypeId, out data))
                {
                    isPlayer = !(MyAPIGateway.Multiplayer.IsServer && MyAPIGateway.Utilities.IsDedicated);
                    subparts = new MyEntitySubpart[ConveyorHingesMod.SUBPART_COUNT];
                    limitRad = MathHelper.ToRadians(data.MaxAngle);
                    maxViewDistSq = data.MaxViewDistance * data.MaxViewDistance;
                    blockLength = block.CubeGrid.GridSize * 0.5f * data.BlockLengthMul;
                    attachRadius = data.AttachRadius;
                    curveStart = Vector3.Backward * blockLength;

                    NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME;
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public override void UpdateAfterSimulation10()
        {
            try
            {
                bool setVisible = CheckVisible();

                if(visible != setVisible)
                    SetSubpartsVisible(setVisible);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        private bool CheckVisible()
        {
            if(lastModelId != block.Model.UniqueId) // on model change
            {
                lastModelId = block.Model.UniqueId;

                var blockDef = (MyCubeBlockDefinition)block.SlimBlock.BlockDefinition;
                hasMainModel = (block.SlimBlock.BuildLevelRatio >= blockDef.CriticalIntegrityRatio);

                if(!hasMainModel)
                    refreshSubparts = true;
            }

            if(!hasMainModel)
                return false;

            if(refreshSubparts || subpartEnd == null || subpartEnd.Closed)
            {
                refreshSubparts = false;
                subpartEnd = GetAndTweakSubpart("PartEnd");

                if(subpartEnd == null)
                {
                    hasMainModel = false;
                    return false;
                }

                for(int i = 0; i < ConveyorHingesMod.SUBPART_COUNT; ++i)
                {
                    var subpart = subparts[i] = GetAndTweakSubpart($"Part{i + 1}");

                    if(subpart == null)
                    {
                        hasMainModel = false;
                        return false;
                    }
                }
            }

            if(!isPlayer) // server needs to set stuff on subparts too, but this is where it stops
                return false;

            var distSq = Vector3D.DistanceSquared(MyAPIGateway.Session.Camera.WorldMatrix.Translation, block.WorldMatrix.Translation);
            var lodCoef = ConveyorHingesMod.Instance.LODcoef; // distance multiplier for resolution and FOV
            distSq *= (lodCoef * lodCoef);

            return (distSq <= maxViewDistSq);
        }

        public override void UpdateAfterSimulation()
        {
            try
            {
                // used to ensure that the limits aren't set beyond the limits allowed by the mod, only needed server side as clients will just spam the network
                if(MyAPIGateway.Multiplayer.IsServer)
                {
                    if(block.LowerLimitRad < -limitRad)
                    {
                        block.LowerLimitRad = -limitRad;
                    }

                    if(block.UpperLimitRad > limitRad)
                    {
                        block.UpperLimitRad = limitRad;
                    }
                }

                if(attachRequestCooldown > 0)
                    attachRequestCooldown--;

                if(isPlayer && visible)
                {
                    UpdateAnimation();
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        private void UpdateAnimation()
        {
            bool hasTop = (block.Top != null);
            var grid = block.CubeGrid;

            var curveEnd = Vector3.Zero;
            var vec = Vector3.Zero;
            var forward = Vector3.Forward;
            var up = Vector3.Up;

            if(hasTop)
            {
                curveEnd = Vector3D.Transform(block.Top.WorldMatrix.Translation + block.Top.WorldMatrix.Forward * blockLength, block.WorldMatrixInvScaled);
            }
            else
            {
                var accelAtPos = grid.Physics.LinearAcceleration + grid.Physics.AngularAcceleration.Cross(block.WorldMatrix.Translation - grid.Physics.CenterOfMassWorld);

                ragdollVelocityLocal += Vector3.TransformNormal(accelAtPos, block.WorldMatrixInvScaled);
                ragdollVelocityLocal *= 0.98f; // viscosity

                ragdollPositionLocal += ragdollVelocityLocal * (1f / 60f) * -0.01f;
                ragdollPositionLocal = Vector3.ClampToSphere(ragdollPositionLocal, blockLength * 1.25f);

                ragdollPositionLocal.Y = MathHelper.Clamp(ragdollPositionLocal.Y, -0.05f, 0.05f);
                ragdollPositionLocal.Z = Math.Min(ragdollPositionLocal.Z, -0.1f);

                curveEnd = ragdollPositionLocal;
            }

            var curveData = ConveyorHingesMod.Instance.curveData;
            var prevVec = curveStart;

            //if(DEBUG_DRAW)
            //{
            //    MyTransparentGeometry.AddPointBillboard(MyStringId.GetOrCompute("WhiteDot"), Color.Lime, Vector3D.Transform(curveStart, block.WorldMatrix), 0.0075f, 0f, blendType: BlendTypeEnum.SDR);
            //}

            for(int i = 0; i < ConveyorHingesMod.SUBPART_COUNT; ++i)
            {
                GetBezierCurve(ref vec, ref curveStart, ref curveHandles, ref curveHandles, ref curveEnd, ref curveData[i]);

                forward = (prevVec - vec);
                up = Vector3.Cross(forward, Vector3.Right);

                subparts[i].PositionComp.SetLocalMatrix(Matrix.CreateWorld(vec, forward, up));

                //if(DEBUG_DRAW)
                //{
                //    var worldPos = Vector3D.Transform(vec, block.WorldMatrix);
                //    MyTransparentGeometry.AddLineBillboard(MyStringId.GetOrCompute("Square"), Color.Red, worldPos, Vector3.TransformNormal(forward, block.WorldMatrix), ConveyorHingesMod.CURVE_TRAVEL, 0.001f, blendType: BlendTypeEnum.SDR);
                //    MyTransparentGeometry.AddPointBillboard(MyStringId.GetOrCompute("WhiteDot"), Color.Red, worldPos, 0.005f, 0f, blendType: BlendTypeEnum.SDR);
                //}

                prevVec = vec;
            }

            //if(DEBUG_DRAW)
            //{
            //    MyTransparentGeometry.AddPointBillboard(MyStringId.GetOrCompute("WhiteDot"), Color.Blue, Vector3D.Transform(curveEnd, block.WorldMatrix), 0.0075f, 0f, blendType: BlendTypeEnum.SDR);
            //}

            if(hasTop)
            {
                var matrix = MatrixD.Transpose(block.WorldMatrix);
                forward = Vector3D.TransformNormal(block.Top.WorldMatrix.Forward, matrix);
                up = Vector3D.TransformNormal(block.Top.WorldMatrix.Up, matrix);

                subpartEnd.PositionComp.SetLocalMatrix(Matrix.CreateWorld(Vector3.Zero, forward, up));
            }
            else
            {
                vec += Vector3.Normalize(forward) * (blockLength * (ConveyorHingesMod.CURVE_OFFSET + ConveyorHingesMod.CURVE_TRAVEL));

                subpartEnd.PositionComp.SetLocalMatrix(Matrix.CreateWorld(vec, -forward, up));
            }

            //if(DEBUG_DRAW)
            //{
            //    MyTransparentGeometry.AddLineBillboard(MyStringId.GetOrCompute("Square"), Color.Magenta, subpartEnd.WorldMatrix.Translation, subpartEnd.WorldMatrix.Forward, ConveyorHingesMod.CURVE_TRAVEL, 0.001f, blendType: BlendTypeEnum.SDR);
            //    MyTransparentGeometry.AddPointBillboard(MyStringId.GetOrCompute("WhiteDot"), Color.Magenta, subpartEnd.WorldMatrix.Translation, 0.005f, 0f, blendType: BlendTypeEnum.SDR);
            //}
        }

        private void SetSubpartsVisible(bool setVisible)
        {
            visible = setVisible;

            if(subpartEnd == null)
                return;

            for(int i = 0; i < ConveyorHingesMod.SUBPART_COUNT; ++i)
            {
                var subpart = subparts[i];

                subpart.Render.Visible = setVisible;
            }

            subpartEnd.Render.Visible = setVisible;
        }

        private MyEntitySubpart GetAndTweakSubpart(string name)
        {
            MyEntitySubpart subpart;

            if(!block.TryGetSubpart(name, out subpart))
                return null;

            subpart.NeedsWorldMatrix = false;
            subpart.IsPreview = true;
            return subpart;
        }

        /// <summary>
        /// <para>Originally from http://devmag.org.za/2011/04/05/bzier-curves-a-tutorial/ </para>
        /// <para>Optimized to use caching.</para>
        /// <para><paramref name="vec"/> is the output.</para>
        /// </summary>
        static void GetBezierCurve(ref Vector3 vec, ref Vector3 p0, ref Vector3 p1, ref Vector3 p2, ref Vector3 p3, ref Vector4 data)
        {
            vec.X = data.X * p0.X + data.Y * p1.X + data.Z * p2.X + data.W * p3.X;
            vec.Y = data.X * p0.Y + data.Y * p1.Y + data.Z * p2.Y + data.W * p3.Y;
            vec.Z = data.X * p0.Z + data.Y * p1.Z + data.Z * p2.Z + data.W * p3.Z;
        }

        /// <summary>
        /// Finds a suitable 
        /// </summary>
        /// <param name="showMessages"></param>
        public void FindAndAttach(bool showMessages = false)
        {
            if(block.IsAttached || attachRequestCooldown > 0)
                return;

            attachRequestCooldown = 15; // prevent this method from executing for this many ticks

            var sphere = new BoundingSphereD(block.GetPosition(), attachRadius);
            var radiusSq = attachRadius * attachRadius;
            var ents = ConveyorHingesMod.Instance.ents;
            var blocks = ConveyorHingesMod.Instance.blocks;

            ents.Clear();
            MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, ents, MyEntityQueryType.Both);

            byte messageType = 0;
            int rollAngle = 0;

            foreach(var ent in ents)
            {
                var grid = ent as MyCubeGrid;

                if(grid == null || grid.MarkedForClose || grid.Physics == null || grid.IsPreview || grid == block.CubeGrid)
                    continue;

                grid.Hierarchy?.QuerySphere(ref sphere, blocks);

                foreach(var b in blocks)
                {
                    var top = b as IMyAttachableTopBlock;

                    if(top == null || top.MarkedForClose)
                        continue;

                    if(Vector3D.DistanceSquared(block.GetPosition(), top.GetPosition()) > radiusSq)
                        continue;

                    if(!ConveyorHingesMod.Instance.HingeTops.Contains(top.SlimBlock.BlockDefinition.Id.SubtypeId))
                    {
                        messageType = Math.Max(messageType, (byte)1);
                        continue;
                    }

                    var dot = Vector3D.Dot(block.WorldMatrix.Up, top.WorldMatrix.Up);
                    if(dot < 0.9f)
                    {
                        messageType = Math.Max(messageType, (byte)2);
                        rollAngle = (int)MathHelper.ToDegrees(Math.Acos(dot));
                        continue;
                    }

                    if(MyAPIGateway.Multiplayer.IsServer)
                    {
                        block.Attach(top);
                    }
                    else
                    {
                        var bytes = BitConverter.GetBytes(block.EntityId);
                        MyAPIGateway.Multiplayer.SendMessageToServer(ConveyorHingesMod.PACKET_ID, bytes, true);
                    }

                    blocks.Clear();
                    ents.Clear();
                    return;
                }

                blocks.Clear();
            }

            ents.Clear();

            if(showMessages)
            {
                switch(messageType)
                {
                    case 0: MyAPIGateway.Utilities.ShowNotification("No nearby hinge top to attach to.", 3000, MyFontEnum.White); break;
                    case 1: MyAPIGateway.Utilities.ShowNotification("Can only attach to conveyor hinge top parts!", 3000, MyFontEnum.Red); break;
                    case 2: MyAPIGateway.Utilities.ShowNotification($"Nearby hinge top roll is off by {rollAngle} degrees.", 3000, MyFontEnum.Red); break;
                }
            }
        }
    }
}