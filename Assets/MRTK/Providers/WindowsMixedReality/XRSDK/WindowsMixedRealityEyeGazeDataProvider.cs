﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.Utilities;
using System;

// These versions represent the first version eye tracking became usable across Unity 2019/2020/2021
// WMR_2_7_0_OR_NEWER stops being defined at 3.0 and WMR_4_4_2_OR_NEWER stops being defined at 5.0, exclusive
#if WMR_2_7_0_OR_NEWER || WMR_4_4_2_OR_NEWER || WMR_5_2_2_OR_NEWER
using Unity.Profiling;
using Unity.XR.WindowsMR;
using UnityEngine;
using UnityEngine.XR;
#endif // WMR_2_7_0_OR_NEWER || WMR_4_4_2_OR_NEWER || WMR_5_2_2_OR_NEWER

namespace Microsoft.MixedReality.Toolkit.XRSDK.WindowsMixedReality
{
    [MixedRealityDataProvider(
        typeof(IMixedRealityInputSystem),
        SupportedPlatforms.WindowsUniversal,
        "XRSDK Windows Mixed Reality Eye Gaze Provider",
        "Profiles/DefaultMixedRealityEyeTrackingProfile.asset", "MixedRealityToolkit.SDK",
        true)]
    public class WindowsMixedRealityEyeGazeDataProvider : BaseInputDeviceManager, IMixedRealityEyeGazeDataProvider, IMixedRealityEyeSaccadeProvider, IMixedRealityCapabilityCheck
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="inputSystem">The <see cref="Microsoft.MixedReality.Toolkit.Input.IMixedRealityInputSystem"/> instance that receives data from this provider.</param>
        /// <param name="name">Friendly name of the service.</param>
        /// <param name="priority">Service priority. Used to determine order of instantiation.</param>
        /// <param name="profile">The service's configuration profile.</param>
        public WindowsMixedRealityEyeGazeDataProvider(
            IMixedRealityInputSystem inputSystem,
            string name,
            uint priority,
            BaseMixedRealityProfile profile) : base(inputSystem, name, priority, profile)
        {
            gazeSmoother = new EyeGazeSmoother();

            // Register for these events to forward along, in case code is still registering for the obsolete actions
            gazeSmoother.OnSaccade += GazeSmoother_OnSaccade;
            gazeSmoother.OnSaccadeX += GazeSmoother_OnSaccadeX;
            gazeSmoother.OnSaccadeY += GazeSmoother_OnSaccadeY;
        }

        /// <inheritdoc />
        public bool SmoothEyeTracking { get; set; } = false;

        /// <inheritdoc />
        public IMixedRealityEyeSaccadeProvider SaccadeProvider => gazeSmoother;
        private readonly EyeGazeSmoother gazeSmoother;

        /// <inheritdoc />
        [Obsolete("Register for this provider's SaccadeProvider's actions instead")]
        public event Action OnSaccade;
        private void GazeSmoother_OnSaccade() => OnSaccade?.Invoke();

        /// <inheritdoc />
        [Obsolete("Register for this provider's SaccadeProvider's actions instead")]
        public event Action OnSaccadeX;
        private void GazeSmoother_OnSaccadeX() => OnSaccadeX?.Invoke();

        /// <inheritdoc />
        [Obsolete("Register for this provider's SaccadeProvider's actions instead")]
        public event Action OnSaccadeY;
        private void GazeSmoother_OnSaccadeY() => OnSaccadeY?.Invoke();

        #region IMixedRealityCapabilityCheck Implementation

        /// <inheritdoc />
        public bool CheckCapability(MixedRealityCapability capability) =>
#if WMR_2_7_0_OR_NEWER || WMR_4_4_2_OR_NEWER || WMR_5_2_2_OR_NEWER
                                                                          capability == MixedRealityCapability.EyeTracking
                                                                          && centerEye.isValid
                                                                          && centerEye.TryGetFeatureValue(WindowsMRUsages.EyeGazeAvailable, out bool gazeAvailable)
                                                                          && gazeAvailable;
#else
                                                                          false;
#endif // WMR_2_7_0_OR_NEWER || WMR_4_4_2_OR_NEWER || WMR_5_2_2_OR_NEWER

        #endregion IMixedRealityCapabilityCheck Implementation

#if WMR_2_7_0_OR_NEWER || WMR_4_4_2_OR_NEWER || WMR_5_2_2_OR_NEWER
        private InputDevice centerEye = default(InputDevice);

        /// <inheritdoc />
        public override void Initialize()
        {
#if UNITY_EDITOR && UNITY_WSA && UNITY_2019_3_OR_NEWER
            Utilities.Editor.UWPCapabilityUtility.RequireCapability(
                    UnityEditor.PlayerSettings.WSACapability.GazeInput,
                    GetType());
#endif // UNITY_EDITOR && UNITY_WSA && UNITY_2019_3_OR_NEWER

            ReadProfile();

            // Call the base after initialization to ensure any early exits do not
            // artificially declare the service as initialized.
            base.Initialize();
        }

        private void ReadProfile()
        {
            if (ConfigurationProfile == null)
            {
                Debug.LogError($"{Name} requires a configuration profile to run properly.");
                return;
            }

            MixedRealityEyeTrackingProfile profile = ConfigurationProfile as MixedRealityEyeTrackingProfile;
            if (profile == null)
            {
                Debug.LogError($"{Name}'s configuration profile must be a MixedRealityEyeTrackingProfile.");
                return;
            }

            SmoothEyeTracking = profile.SmoothEyeTracking;
        }

        private static readonly ProfilerMarker UpdatePerfMarker = new ProfilerMarker("[MRTK] WindowsMixedRealityEyeGazeDataProvider.Update");

        /// <inheritdoc />
        public override void Update()
        {
            using (UpdatePerfMarker.Auto())
            {
                if (!centerEye.isValid)
                {
                    centerEye = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
                    if (!centerEye.isValid)
                    {
                        Service?.EyeGazeProvider?.UpdateEyeTrackingStatus(this, false);
                        return;
                    }
                }

                if (!centerEye.TryGetFeatureValue(WindowsMRUsages.EyeGazeAvailable, out bool gazeAvailable) || !gazeAvailable)
                {
                    Service?.EyeGazeProvider?.UpdateEyeTrackingStatus(this, false);
                    return;
                }

                Service?.EyeGazeProvider?.UpdateEyeTrackingStatus(this, true);

                if (centerEye.TryGetFeatureValue(WindowsMRUsages.EyeGazeTracked, out bool gazeTracked)
                    && gazeTracked
                    && centerEye.TryGetFeatureValue(WindowsMRUsages.EyeGazePosition, out Vector3 eyeGazePosition)
                    && centerEye.TryGetFeatureValue(WindowsMRUsages.EyeGazeRotation, out Quaternion eyeGazeRotation))
                {
                    Vector3 worldPosition = MixedRealityPlayspace.TransformPoint(eyeGazePosition);
                    Vector3 worldRotation = MixedRealityPlayspace.TransformDirection(eyeGazeRotation * Vector3.forward);

                    Ray newGaze = new Ray(worldPosition, worldRotation);

                    if (SmoothEyeTracking)
                    {
                        newGaze = gazeSmoother.SmoothGaze(newGaze);
                    }

                    Service?.EyeGazeProvider?.UpdateEyeGaze(this, newGaze, DateTime.UtcNow);
                }
            }
        }
#endif // WMR_2_7_0_OR_NEWER || WMR_4_4_2_OR_NEWER || WMR_5_2_2_OR_NEWER
    }
}
