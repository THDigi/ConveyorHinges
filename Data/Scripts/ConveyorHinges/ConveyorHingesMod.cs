using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
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
            Log.ModName = "Conveyor Hinges";

            CopyStats();
        }

        public static ConveyorHingesMod Instance = null;

        public bool IsInitialized = false;
        public bool IsPlayer = false;
        public float LODcoef = 1f;
        public Vector4[] CurveData;

        private byte skipLODcheck = 0;
        private float referenceHFOVtangent = 0;

        private bool parsedTerminalControls = false;
        private IMyTerminalControlSlider sliderVelocity;
        private IMyTerminalControlSlider sliderLowerLimit;
        private IMyTerminalControlSlider sliderUpperLimit;
        private Action<IMyTerminalBlock, float> sliderVelocitySetter;
        private Action<IMyTerminalBlock, float> sliderLowerLimitSetter;
        private Action<IMyTerminalBlock, float> sliderUpperLimitSetter;
        private Action<IMyTerminalBlock> attachSetter;
        private Action<IMyTerminalBlock> attachAction;

        public readonly List<MyEntity> Ents = new List<MyEntity>();
        public readonly List<MyEntity> Blocks = new List<MyEntity>();

        public readonly string[] SubpartNames = new string[SUBPART_COUNT];

        public const ushort PACKET_ID = 8606; // used to send the attach action to server

        public const float UNLIMITED = 361f;
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
            public float AttachRadius;
            public MyDefinitionId CopyStatsFromBase;
            public MyDefinitionId CopyStatsFromTop;
        }

        public readonly Dictionary<MyDefinitionId, HingeData> Hinges = new Dictionary<MyDefinitionId, HingeData>(MyDefinitionId.Comparer)
        {
            [new MyDefinitionId(typeof(MyObjectBuilder_MotorAdvancedStator), SMALL_STATOR)] = new HingeData()
            {
                MaxAngle = 90,
                MaxViewDistance = 75,
                BlockLengthMul = 1,
                AttachRadius = 0.1f,
                CopyStatsFromBase = new MyDefinitionId(typeof(MyObjectBuilder_MotorStator), "SmallStator"),
                CopyStatsFromTop = new MyDefinitionId(typeof(MyObjectBuilder_MotorRotor), "SmallRotor"),
            },
            [new MyDefinitionId(typeof(MyObjectBuilder_MotorAdvancedStator), MEDIUM_STATOR)] = new HingeData()
            {
                MaxAngle = 90,
                MaxViewDistance = 200,
                BlockLengthMul = 3,
                AttachRadius = 0.25f,
                CopyStatsFromBase = new MyDefinitionId(typeof(MyObjectBuilder_MotorAdvancedStator), "SmallAdvancedStator"),
                CopyStatsFromTop = new MyDefinitionId(typeof(MyObjectBuilder_MotorAdvancedRotor), "SmallAdvancedRotor"),
            },
            [new MyDefinitionId(typeof(MyObjectBuilder_MotorAdvancedStator), LARGE_STATOR)] = new HingeData()
            {
                MaxAngle = 90,
                MaxViewDistance = 300,
                BlockLengthMul = 1,
                AttachRadius = 0.5f,
                CopyStatsFromBase = new MyDefinitionId(typeof(MyObjectBuilder_MotorAdvancedStator), "LargeAdvancedStator"),
                CopyStatsFromTop = new MyDefinitionId(typeof(MyObjectBuilder_MotorAdvancedRotor), "LargeAdvancedRotor"),
            },
        };

        public readonly HashSet<MyStringHash> HingeTops = new HashSet<MyStringHash>(MyStringHash.Comparer)
        {
            MyStringHash.GetOrCompute("SmallConveyorHingeHead"),
            MyStringHash.GetOrCompute("MediumConveyorHingeHead"),
            MyStringHash.GetOrCompute("LargeConveyorHingeHead"),
        };

        void CopyStats()
        {
            foreach(var kv in Hinges)
            {
                var hingeId = kv.Key;
                var hingeData = kv.Value;

                MyMotorStatorDefinition hingeBaseDef = MyDefinitionManager.Static.GetCubeBlockDefinition(hingeId) as MyMotorStatorDefinition;
                if(hingeBaseDef == null)
                {
                    Log.Error($"Can't find block definition for '{hingeId.ToString()}'");
                    continue;
                }

                MyCubeBlockDefinition hingeTopDef = GetTopDef(hingeBaseDef);
                if(hingeTopDef == null)
                {
                    Log.Error($"Can't find block definition for '{hingeBaseDef.TopPart}' at {hingeBaseDef.CubeSize.ToString()} size");
                    continue;
                }

                MyMotorStatorDefinition copyBaseDef = MyDefinitionManager.Static.GetCubeBlockDefinition(hingeData.CopyStatsFromBase) as MyMotorStatorDefinition;
                MyCubeBlockDefinition copyTopDef = MyDefinitionManager.Static.GetCubeBlockDefinition(hingeData.CopyStatsFromTop);

                if(copyBaseDef == null)
                {
                    Log.Error($"Can't find block definition for '{hingeData.CopyStatsFromBase.ToString()}' to copy stats for '{hingeId.ToString()}'");
                    continue;
                }

                if(copyTopDef == null)
                {
                    Log.Error($"Can't find block definition for '{hingeData.CopyStatsFromTop.ToString()}' to copy stats for '{hingeId.ToString()}'");
                    continue;
                }

                hingeBaseDef.MaxForceMagnitude = copyBaseDef.MaxForceMagnitude;
                hingeBaseDef.UnsafeTorqueThreshold = copyBaseDef.UnsafeTorqueThreshold;

                hingeBaseDef.SafetyDetach = copyBaseDef.SafetyDetach;
                hingeBaseDef.SafetyDetachMin = copyBaseDef.SafetyDetachMin;
                hingeBaseDef.SafetyDetachMax = copyBaseDef.SafetyDetachMax;

                hingeBaseDef.RequiredPowerInput = copyBaseDef.RequiredPowerInput;

                CopyGenericStatsFor(hingeBaseDef, copyBaseDef);
                CopyGenericStatsFor(hingeTopDef, copyTopDef);

                //Log.Info($"Copied stats from '{hingeData.CopyStatsFromBase.ToString()}' to '{hingeId.ToString()}' (and rotor top too)");
            }
        }

        void CopyGenericStatsFor(MyCubeBlockDefinition hingeDef, MyCubeBlockDefinition copyDef)
        {
            hingeDef.PCU = copyDef.PCU;
            hingeDef.Mass = copyDef.Mass;

            //hingeDef.Components = copyDef.Components;
            //hingeDef.CriticalIntegrityRatio = copyDef.CriticalIntegrityRatio;
            //hingeDef.IntegrityPointsPerSec = copyDef.IntegrityPointsPerSec;
            //hingeDef.MaxIntegrity = copyDef.MaxIntegrity;
            //hingeDef.MaxIntegrityRatio = copyDef.MaxIntegrityRatio;
            //hingeDef.OwnershipIntegrityRatio = copyDef.OwnershipIntegrityRatio;
        }

        MyCubeBlockDefinition GetTopDef(MyMotorStatorDefinition def)
        {
            var group = MyDefinitionManager.Static.TryGetDefinitionGroup(def.TopPart);

            if(group == null)
            {
                Log.Error($"Can't find blockpair named '{def.TopPart}'");
                return null;
            }

            return (def.CubeSize == MyCubeSize.Large ? group.Large : group.Small);
        }

        public override void BeforeStart()
        {
            IsInitialized = true;
            IsPlayer = !(MyAPIGateway.Multiplayer.IsServer && MyAPIGateway.Utilities.IsDedicated);

            for(int i = 0; i < SubpartNames.Length; ++i)
            {
                SubpartNames[i] = "Part" + (i + 1).ToString();
            }

            if(MyAPIGateway.Multiplayer.IsServer)
            {
                MyAPIGateway.Multiplayer.RegisterMessageHandler(PACKET_ID, ReceivedPacket);
            }

            if(IsPlayer)
            {
                SetUpdateOrder(MyUpdateOrder.AfterSimulation);

                // precalculate curve data
                CurveData = new Vector4[SUBPART_COUNT];

                for(int i = 0; i < CurveData.Length; ++i)
                {
                    var t = CURVE_OFFSET + (i * CURVE_TRAVEL);

                    var rt = 1 - t;
                    var rtt = rt * t;
                    var p0mul = rt * rt * rt;
                    var p1mul = 3 * rt * rtt;
                    var p2mul = 3 * rtt * t;
                    var p3mul = t * t * t;

                    CurveData[i] = new Vector4(p0mul, p1mul, p2mul, p3mul);
                }
            }
        }

        protected override void UnloadData()
        {
            Instance = null;
            IsInitialized = false;
            Log.Close();

            MyAPIGateway.Multiplayer.UnregisterMessageHandler(PACKET_ID, ReceivedPacket);
        }

        public override void UpdateAfterSimulation()
        {
            try
            {
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

        private void ReceivedPacket(byte[] bytes)
        {
            try
            {
                var id = BitConverter.ToInt64(bytes, 0);
                var block = MyEntities.GetEntityById(id);
                block?.GameLogic.GetAs<HingeBlock>()?.FindAndAttach();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        #region Edit terminal controls
        public static void SetupControls()
        {
            if(Instance.parsedTerminalControls)
                return;

            Instance.parsedTerminalControls = true;

            var isNotHingeFunc = new Func<IMyTerminalBlock, bool>(IsNotHinge);

            List<IMyTerminalControl> controls;
            MyAPIGateway.TerminalControls.GetControls<IMyMotorAdvancedStator>(out controls);
            IMyTerminalControlSlider slider;

            foreach(var c in controls)
            {
                switch(c.Id)
                {
                    // overwritten/appended-to terminal functions
                    case "Attach":
                        var button = (IMyTerminalControlButton)c;

                        if(button.Action != null)
                            Instance.attachSetter = button.Action;

                        button.Action = Action_Attach;
                        break;

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

                    // these are hidden for hinges
                    case "Add Small Top Part": // requires a special model to fit properly
                    case "Displacement": // displacement has no useful purpose for this block
                        c.Visible = CombineFunc.Create(c.Visible, isNotHingeFunc);
                        break;
                }
            }

            List<IMyTerminalAction> actions;
            MyAPIGateway.TerminalControls.GetActions<IMyMotorAdvancedStator>(out actions);

            foreach(var a in actions)
            {
                switch(a.Id)
                {
                    case "Attach":
                        if(a.Action != null)
                            Instance.attachAction = a.Action;
                        a.Action = Action_Attach;
                        break;

                    case "Add Small Top Part": // requires a special model to fit properly
                    case "IncreaseDisplacement": // displacement has no useful purpose for this block
                    case "DecreaseDisplacement":
                    case "ResetDisplacement":
                        a.Enabled = CombineFunc.Create(a.Enabled, isNotHingeFunc);
                        break;
                }
            }
        }

        private static bool IsNotHinge(IMyTerminalBlock b)
        {
            try
            {
                return !Instance.Hinges.ContainsKey(b.SlimBlock.BlockDefinition.Id);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            return true;
        }

        private static void Action_Attach(IMyTerminalBlock b)
        {
            try
            {
                // replacing attach method because the vanilla one is hardcoded for vanilla blocks.
                // also my method does additional checks to prevent attaching the top part the wrong way around which would cause clang.
                if(Instance.Hinges.ContainsKey(b.SlimBlock.BlockDefinition.Id))
                {
                    b.GameLogic?.GetAs<HingeBlock>()?.FindAndAttach(showMessages: true);
                }
                else
                {
                    Instance.attachAction?.Invoke(b);
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        private static float Slider_LowerMin(IMyTerminalBlock b)
        {
            return -GetHingeMaxAngle(b, UNLIMITED);
        }

        private static float Slider_LowerMax(IMyTerminalBlock b)
        {
            return GetHingeMaxAngle(b, 360f);
        }

        private static float Slider_UpperMin(IMyTerminalBlock b)
        {
            return -GetHingeMaxAngle(b, 360f);
        }

        private static float Slider_UpperMax(IMyTerminalBlock b)
        {
            return GetHingeMaxAngle(b, UNLIMITED);
        }

        private static float GetHingeMaxAngle(IMyTerminalBlock b, float defaultValue)
        {
            try
            {
                HingeData data;
                if(Instance.Hinges.TryGetValue(b.SlimBlock.BlockDefinition.Id, out data))
                    return data.MaxAngle;
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            return defaultValue;
        }

        private static void Slider_VelocitySetter(IMyTerminalBlock b, float v)
        {
            try
            {
                if(Instance.Hinges.ContainsKey(b.SlimBlock.BlockDefinition.Id))
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
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        private static void Slider_LowerSetter(IMyTerminalBlock b, float v)
        {
            try
            {
                if(Instance.Hinges.ContainsKey(b.SlimBlock.BlockDefinition.Id))
                {
                    var stator = (IMyMotorAdvancedStator)b;

                    if(stator.TargetVelocityRPM < 0 && (stator.Angle + LIMIT_OFFSET_RAD) < MathHelper.ToRadians(v))
                    {
                        stator.TargetVelocityRPM = 0;
                    }
                }

                Instance.sliderLowerLimitSetter?.Invoke(b, v);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        private static void Slider_UpperSetter(IMyTerminalBlock b, float v)
        {
            try
            {
                if(Instance.Hinges.ContainsKey(b.SlimBlock.BlockDefinition.Id))
                {
                    var stator = (IMyMotorAdvancedStator)b;

                    if(stator.TargetVelocityRPM > 0 && (stator.Angle - LIMIT_OFFSET_RAD) > MathHelper.ToRadians(v))
                    {
                        stator.TargetVelocityRPM = 0;
                    }
                }

                Instance.sliderUpperLimitSetter?.Invoke(b, v);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        #endregion
    }
}