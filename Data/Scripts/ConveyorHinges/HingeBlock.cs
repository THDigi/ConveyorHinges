using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Digi.ConveyorHinges
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_MotorAdvancedStator), false, ConveyorHingesMod.SMALL_STATOR, ConveyorHingesMod.MEDIUM_STATOR, ConveyorHingesMod.LARGE_STATOR)]
    public class ConveyorHinge : MyGameLogicComponent
    {
        private IMyMotorAdvancedStator block;
        private float limitRad;

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

                if(block.CubeGrid.Physics != null && ConveyorHingesMod.Instance.HingeLimitsDeg.TryGetValue(block.SlimBlock.BlockDefinition.Id.SubtypeId, out limitRad))
                {
                    limitRad = MathHelper.ToRadians(limitRad);
                    NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        
        //public override void UpdateBeforeSimulation()
        public override void UpdateAfterSimulation()
        {
            try
            {
                // ensure limits don't go beyond their spec

                if(block.LowerLimitRad < -limitRad)
                {
                    block.LowerLimitRad = -limitRad;
                }

                if(block.UpperLimitRad > limitRad)
                {
                    block.UpperLimitRad = limitRad;
                }
                

                //if(block.Angle > ConveyorHingesMod.LIMIT_BEYOND_RAD)
                //{
                //    block.TargetVelocityRPM = -30f;
                //}
                //else if(block.Angle < -ConveyorHingesMod.LIMIT_BEYOND_RAD)
                //{
                //    block.TargetVelocityRPM = 30f;
                //}

                //if(Math.Abs(block.LowerLimitRad - prevLowerLimitRad) > 0.0001f)
                //{
                //    block.TargetVelocityRad = 0;
                //    prevLowerLimitRad = block.LowerLimitRad;
                //}

                //if(Math.Abs(block.UpperLimitRad - prevUpperLimitRad) > 0.0001f)
                //{
                //    block.TargetVelocityRad = 0;
                //    prevUpperLimitRad = block.UpperLimitRad;
                //}

                //if(Math.Abs(block.Angle) > ConveyorHingesMod.LIMIT_BEYOND_RAD)
                //{
                //    if((block.TargetVelocityRad > 0 && block.Angle > 0) || (block.TargetVelocityRad < 0 && block.Angle < 0))
                //    {
                //        block.TargetVelocityRad = 0;
                //    }
                //}

                //if(Math.Abs(block.Angle) > ConveyorHingesMod.LIMIT_BEYOND_RAD)
                //{
                //    if(Math.Abs(prevTargetVelRad) < 0.0001f)
                //        prevTargetVelRad = block.TargetVelocityRad;

                //    block.TargetVelocityRad = -prevTargetVelRad;
                //}
                //else if(Math.Abs(block.Angle) < ConveyorHingesMod.LIMIT_RAD && Math.Abs(prevTargetVelRad) > 0)
                //{
                //    block.TargetVelocityRad = prevTargetVelRad;
                //    prevTargetVelRad = 0f;
                //}
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
    }
}