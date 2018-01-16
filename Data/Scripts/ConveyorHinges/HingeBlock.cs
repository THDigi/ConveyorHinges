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

                if(MyAPIGateway.Multiplayer.IsServer)
                {
                    block = (IMyMotorAdvancedStator)Entity;

                    if(block.CubeGrid.Physics != null && ConveyorHingesMod.Instance.HingeLimitsDeg.TryGetValue(block.SlimBlock.BlockDefinition.Id.SubtypeId, out limitRad))
                    {
                        limitRad = MathHelper.ToRadians(limitRad);
                        NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        // only called server side because of the condition in UpdateOnceBeforeFrame()
        // used to ensure that the limits aren't set beyond the limits allowed by the mod.
        public override void UpdateAfterSimulation()
        {
            try
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
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
    }
}