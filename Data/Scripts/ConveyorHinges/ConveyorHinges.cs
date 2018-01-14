using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.Components;
using VRage.Utils;
using VRageMath;

namespace Digi.ConveyorHinges
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class ConveyorHingesMod : MySessionComponentBase
    {
        public override void LoadData()
        {
            Instance = this;
            Log.SetUp("Conveyor Hinges", 385778606, "ConveyorHinges");
        }

        public static ConveyorHingesMod Instance = null;

        public bool IsInitialized = false;

        private bool parsedTerminalControls = false;
        private IMyTerminalControlSlider sliderVelocity;
        private IMyTerminalControlSlider sliderLowerLimit;
        private IMyTerminalControlSlider sliderUpperLimit;
        private Action<IMyTerminalBlock, float> sliderVelocitySetter;
        private Action<IMyTerminalBlock, float> sliderLowerLimitSetter;
        private Action<IMyTerminalBlock, float> sliderUpperLimitSetter;
        private Func<IMyTerminalBlock, bool> addSmallTopPartVisible;
        private Func<IMyTerminalBlock, bool> displacementVisible;
        private readonly Dictionary<string, Func<IMyTerminalBlock, bool>> actionEnabled = new Dictionary<string, Func<IMyTerminalBlock, bool>>();
        
        private const float UNLIMITED = 361f;
        public const float LIMIT_OFFSET_RAD = 1f / 180f * MathHelper.Pi;

        public const string SMALL_STATOR = "SmallConveyorHinge";
        public const string MEDIUM_STATOR = "MediumConveyorHinge";
        public const string LARGE_STATOR = "LargeConveyorHinge";

        public readonly Dictionary<MyStringHash, float> HingeLimitsDeg = new Dictionary<MyStringHash, float>()
        {
            [MyStringHash.GetOrCompute(SMALL_STATOR)] = 90,
            [MyStringHash.GetOrCompute(MEDIUM_STATOR)] = 90,
            [MyStringHash.GetOrCompute(LARGE_STATOR)] = 110
        };

        public void Init()
        {
            IsInitialized = true;
            Log.Init();
            MyAPIGateway.Utilities.InvokeOnGameThread(() => SetUpdateOrder(MyUpdateOrder.NoUpdate)); // stop updating this component, needed as invoke because it can't be changed mid-update.
        }

        protected override void UnloadData()
        {
            Instance = null;
            IsInitialized = false;
            Log.Close();
        }

        public override void UpdateAfterSimulation()
        {
            try
            {
                if(!IsInitialized)
                {
                    if(MyAPIGateway.Session == null)
                        return;

                    Init();
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        #region Edit terminal controls
        public static void SetupControls<T>()
        {
            if(Instance.parsedTerminalControls)
                return;

            Instance.parsedTerminalControls = true;

            var controls = new List<IMyTerminalControl>();
            MyAPIGateway.TerminalControls.GetControls<T>(out controls);
            IMyTerminalControlSlider slider;

            foreach(var c in controls)
            {
                switch(c.Id)
                {
                    case "LowerLimit":
                        slider = Instance.sliderLowerLimit = (IMyTerminalControlSlider)c;
                        slider.SetLimits(Slider_LowerMin, Slider_LowerMax);

                        if(slider.Setter != null)
                            Instance.sliderLowerLimitSetter = slider.Setter;

                        slider.Setter = Slider_LowerSetter;
                        break;

                    case "UpperLimit":
                        slider = Instance.sliderUpperLimit = (IMyTerminalControlSlider)c;
                        slider.SetLimits(Slider_UpperMin, Slider_UpperMax);

                        if(slider.Setter != null)
                            Instance.sliderUpperLimitSetter = slider.Setter;

                        slider.Setter = Slider_UpperSetter;
                        break;

                    case "Velocity": // hook into velocity setter to avoid loop-around limits
                        slider = Instance.sliderVelocity = (IMyTerminalControlSlider)c;

                        if(slider.Setter != null)
                            Instance.sliderVelocitySetter = slider.Setter;

                        slider.Setter = Slider_VelocitySetter;
                        break;

                    case "Add Small Top Part": // TODO removed because requires a special model to fit properly
                        if(c.Visible != null)
                            Instance.addSmallTopPartVisible = c.Visible;

                        c.Visible = Control_AddSmallTopPartVisible;
                        break;

                    case "Displacement":
                        if(c.Visible != null)
                            Instance.displacementVisible = c.Visible;

                        c.Visible = Control_DisplacementVisible;
                        break;
                }
            }

            var hideActionIds = new HashSet<string>()
            {
                "Add Small Top Part", // TODO removed because requires a special model to fit properly
                "IncreaseDisplacement",
                "DecreaseDisplacement",
                "ResetDisplacement",
            };

            var actions = new List<IMyTerminalAction>();
            MyAPIGateway.TerminalControls.GetActions<IMyMotorAdvancedStator>(out actions);

            foreach(var a in actions)
            {
                string id = a.Id;

                if(hideActionIds.Contains(id))
                {
                    if(a.Enabled != null)
                        Instance.actionEnabled[id] = a.Enabled;

                    a.Enabled = (b) =>
                    {
                        var func = Instance.actionEnabled.GetValueOrDefault(id, null);
                        return (func == null ? true : func.Invoke(b)) && !Instance.HingeLimitsDeg.ContainsKey(b.SlimBlock.BlockDefinition.Id.SubtypeId);
                    };
                }
            }
        }

        private static float Slider_LowerMin(IMyTerminalBlock b)
        {
            return -Instance.HingeLimitsDeg.GetValueOrDefault(b.SlimBlock.BlockDefinition.Id.SubtypeId, UNLIMITED);
        }

        private static float Slider_LowerMax(IMyTerminalBlock b)
        {
            return Instance.HingeLimitsDeg.GetValueOrDefault(b.SlimBlock.BlockDefinition.Id.SubtypeId, 360f);
        }

        private static float Slider_UpperMin(IMyTerminalBlock b)
        {
            return -Instance.HingeLimitsDeg.GetValueOrDefault(b.SlimBlock.BlockDefinition.Id.SubtypeId, 360f);
        }

        private static float Slider_UpperMax(IMyTerminalBlock b)
        {
            return Instance.HingeLimitsDeg.GetValueOrDefault(b.SlimBlock.BlockDefinition.Id.SubtypeId, UNLIMITED);
        }

        private static void Slider_VelocitySetter(IMyTerminalBlock b, float v)
        {
            if(Instance.HingeLimitsDeg.ContainsKey(b.SlimBlock.BlockDefinition.Id.SubtypeId))
            {
                var stator = (IMyMotorAdvancedStator)b;

                if((stator.Angle < (stator.LowerLimitRad - LIMIT_OFFSET_RAD) && v < 0) || (stator.Angle > (stator.UpperLimitRad + LIMIT_OFFSET_RAD) && v > 0))
                {
                    Instance.sliderVelocitySetter?.Invoke(b, 0);
                    Instance.sliderVelocity.UpdateVisual();
                    return;
                }
            }

            Instance.sliderVelocitySetter?.Invoke(b, v);
        }

        private static void Slider_LowerSetter(IMyTerminalBlock b, float v)
        {
            if(Instance.HingeLimitsDeg.ContainsKey(b.SlimBlock.BlockDefinition.Id.SubtypeId))
            {
                var stator = (IMyMotorAdvancedStator)b;

                if(Math.Abs(stator.TargetVelocityRPM) > 0 && stator.Angle < MathHelper.ToRadians(v))
                {
                    stator.TargetVelocityRPM = 0;
                }
            }

            Instance.sliderLowerLimitSetter?.Invoke(b, v);
        }

        private static void Slider_UpperSetter(IMyTerminalBlock b, float v)
        {
            if(Instance.HingeLimitsDeg.ContainsKey(b.SlimBlock.BlockDefinition.Id.SubtypeId))
            {
                var stator = (IMyMotorAdvancedStator)b;

                if(Math.Abs(stator.TargetVelocityRPM) > 0 && stator.Angle > MathHelper.ToRadians(v))
                {
                    stator.TargetVelocityRPM = 0;
                }
            }

            Instance.sliderUpperLimitSetter?.Invoke(b, v);
        }

        private static bool Control_AddSmallTopPartVisible(IMyTerminalBlock b)
        {
            var func = Instance.addSmallTopPartVisible;
            return (func == null ? true : func.Invoke(b)) && !Instance.HingeLimitsDeg.ContainsKey(b.SlimBlock.BlockDefinition.Id.SubtypeId);
        }

        private static bool Control_DisplacementVisible(IMyTerminalBlock b)
        {
            var func = Instance.displacementVisible;
            return (func == null ? true : func.Invoke(b)) && !Instance.HingeLimitsDeg.ContainsKey(b.SlimBlock.BlockDefinition.Id.SubtypeId);
        }
        #endregion
    }
}