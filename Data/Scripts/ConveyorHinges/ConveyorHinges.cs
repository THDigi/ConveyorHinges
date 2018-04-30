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
        public bool IsPlayer = false;
        public float LODcoef = 1f;
        public Vector4[] curveData;

        private byte skipLODcheck = 0;
        private float referenceHFOVtangent = 0;

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

        public const int SUBPART_COUNT = 5;
        public const float CURVE_OFFSET = 0.25f;
        public const float CURVE_TRAVEL = 0.125f;

        public struct HingeData
        {
            public float MaxAngle;
            public float MaxViewDistance;
            public float BlockLengthMul;
        }

        public readonly Dictionary<MyStringHash, HingeData> Hinges = new Dictionary<MyStringHash, HingeData>(MyStringHash.Comparer)
        {
            [MyStringHash.GetOrCompute(SMALL_STATOR)] = new HingeData()
            {
                MaxAngle = 90,
                MaxViewDistance = 75,
                BlockLengthMul = 1,
            },
            [MyStringHash.GetOrCompute(MEDIUM_STATOR)] = new HingeData()
            {
                MaxAngle = 90,
                MaxViewDistance = 200,
                BlockLengthMul = 3,
            },
            [MyStringHash.GetOrCompute(LARGE_STATOR)] = new HingeData()
            {
                MaxAngle = 90,
                MaxViewDistance = 300,
                BlockLengthMul = 1,
            },
        };

        private static float GetHingeMaxAngle(MyStringHash subtypeId, float defaultValue)
        {
            HingeData data;

            if(Instance.Hinges.TryGetValue(subtypeId, out data))
                return data.MaxAngle;

            return defaultValue;
        }

        public void Init()
        {
            IsInitialized = true;
            IsPlayer = !(MyAPIGateway.Multiplayer.IsServer && MyAPIGateway.Utilities.IsDedicated);
            Log.Init();

            if(IsPlayer)
            {
                // precalculate curve data
                curveData = new Vector4[SUBPART_COUNT];

                for(int i = 0; i < curveData.Length; ++i)
                {
                    var t = CURVE_OFFSET + (i * CURVE_TRAVEL);

                    var rt = 1 - t;
                    var rtt = rt * t;
                    var p0mul = rt * rt * rt;
                    var p1mul = 3 * rt * rtt;
                    var p2mul = 3 * rtt * t;
                    var p3mul = t * t * t;

                    curveData[i] = new Vector4(p0mul, p1mul, p2mul, p3mul);
                }
            }
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

                if(IsPlayer && ++skipLODcheck >= 30) // 60/<this> = updates per second
                {
                    skipLODcheck = 0;

                    // Game's LOD scaling formula, from VRageRender.MyCommon.MoveToNextFrame()
                    // used for multiplying distances to match the model LOD behavior depending on resolution and FOV
                    const float REFERENCE_HORIZONTAL_FOV = (70f / 180f * MathHelper.Pi) * 0.5f; // to radians, then half
                    const float REFERENCE_RESOLUTION_HEIGHT = 1080;

                    if(referenceHFOVtangent == 0)
                        referenceHFOVtangent = (float)Math.Tan(REFERENCE_HORIZONTAL_FOV);

                    var camera = MyAPIGateway.Session.Camera;
                    var coefFOV = (float)Math.Tan(camera.FovWithZoom * 0.5f) / referenceHFOVtangent;
                    var coefResolution = REFERENCE_RESOLUTION_HEIGHT / (float)camera.ViewportSize.Y;
                    LODcoef = coefFOV * coefResolution;
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

                    case "Add Small Top Part": // removed because requires a special model to fit properly
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
                "Add Small Top Part", // removed because requires a special model to fit properly
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
                        return (func == null ? true : func.Invoke(b)) && !Instance.Hinges.ContainsKey(b.SlimBlock.BlockDefinition.Id.SubtypeId);
                    };
                }
            }
        }

        private static float Slider_LowerMin(IMyTerminalBlock b)
        {
            return -GetHingeMaxAngle(b.SlimBlock.BlockDefinition.Id.SubtypeId, UNLIMITED);
        }

        private static float Slider_LowerMax(IMyTerminalBlock b)
        {
            return GetHingeMaxAngle(b.SlimBlock.BlockDefinition.Id.SubtypeId, 360f);
        }

        private static float Slider_UpperMin(IMyTerminalBlock b)
        {
            return -GetHingeMaxAngle(b.SlimBlock.BlockDefinition.Id.SubtypeId, 360f);
        }

        private static float Slider_UpperMax(IMyTerminalBlock b)
        {
            return GetHingeMaxAngle(b.SlimBlock.BlockDefinition.Id.SubtypeId, UNLIMITED);
        }

        private static void Slider_VelocitySetter(IMyTerminalBlock b, float v)
        {
            if(Instance.Hinges.ContainsKey(b.SlimBlock.BlockDefinition.Id.SubtypeId))
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
            if(Instance.Hinges.ContainsKey(b.SlimBlock.BlockDefinition.Id.SubtypeId))
            {
                var stator = (IMyMotorAdvancedStator)b;

                if(stator.TargetVelocityRPM < 0 && (stator.Angle + LIMIT_OFFSET_RAD) < MathHelper.ToRadians(v))
                {
                    stator.TargetVelocityRPM = 0;
                }
            }

            Instance.sliderLowerLimitSetter?.Invoke(b, v);
        }

        private static void Slider_UpperSetter(IMyTerminalBlock b, float v)
        {
            if(Instance.Hinges.ContainsKey(b.SlimBlock.BlockDefinition.Id.SubtypeId))
            {
                var stator = (IMyMotorAdvancedStator)b;

                if(stator.TargetVelocityRPM > 0 && (stator.Angle - LIMIT_OFFSET_RAD) > MathHelper.ToRadians(v))
                {
                    stator.TargetVelocityRPM = 0;
                }
            }

            Instance.sliderUpperLimitSetter?.Invoke(b, v);
        }

        private static bool Control_AddSmallTopPartVisible(IMyTerminalBlock b)
        {
            var func = Instance.addSmallTopPartVisible;
            return (func == null ? true : func.Invoke(b)) && !Instance.Hinges.ContainsKey(b.SlimBlock.BlockDefinition.Id.SubtypeId);
        }

        private static bool Control_DisplacementVisible(IMyTerminalBlock b)
        {
            var func = Instance.displacementVisible;
            return (func == null ? true : func.Invoke(b)) && !Instance.Hinges.ContainsKey(b.SlimBlock.BlockDefinition.Id.SubtypeId);
        }
        #endregion
    }
}