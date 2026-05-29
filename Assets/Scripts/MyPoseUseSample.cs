/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Oculus.Interaction.Input;
using TMPro;
using UnityEngine;
using UnityEngine.Assertions;

namespace Oculus.Interaction.Samples
{
    /// <summary>
    /// Meta Interaction SDK sample (customized): shows a particle/text visual at each hand pose when selected.
    /// Optionally starts <see cref="VoiceManager"/> listening when a configured pose is chosen.
    /// </summary>
    public class MyPoseUseSample : MonoBehaviour
    {
        [SerializeField, Interface(typeof(IHmd))]
        private UnityEngine.Object _hmd;
        private IHmd Hmd { get; set; }

        [SerializeField]
        private ActiveStateSelector[] _poses;

        [SerializeField]
        private Material[] _onSelectIcons;

        [SerializeField]
        private GameObject _poseActiveVisualPrefab;

        [Header("Voice Trigger")]
        [SerializeField]
        private bool _startListeningOnPoseSelect = false;

        [Tooltip("If set to -1, any selected pose can trigger listening.")]
        [SerializeField]
        private int _voiceTriggerPoseIndex = -1;

        [SerializeField]
        private VoiceManager _voiceManager;

        private GameObject[] _poseActiveVisuals;

        protected virtual void Awake()
        {
            Hmd = _hmd as IHmd;
        }

        protected virtual void Start()
        {
            this.AssertField(_poseActiveVisualPrefab, nameof(_poseActiveVisualPrefab));

            _poseActiveVisuals = new GameObject[_poses.Length];
            for (int i = 0; i < _poses.Length; i++)
            {
                var go = Instantiate(_poseActiveVisualPrefab);
                var tmp = go.GetComponentInChildren<TextMeshPro>();
                if (tmp != null) tmp.text = _poses[i].name;
                var psr = go.GetComponentInChildren<ParticleSystemRenderer>();
                if (psr != null) psr.material = _onSelectIcons[i];
                go.SetActive(false);
                _poseActiveVisuals[i] = go;

                int poseNumber = i;
                _poses[i].WhenSelected += () => ShowVisuals(poseNumber);
                _poses[i].WhenUnselected += () => HideVisuals(poseNumber);
            }
        }

        /// <summary>
        /// Prefers average hand position; falls back to HMD forward when hands are unavailable.
        /// </summary>
        public void ShowVisuals(int poseNumber)
        {
            var visual = _poseActiveVisuals[poseNumber];
            var t = visual.transform;
            if (!TryPlaceVisual(poseNumber, t))
                return;

            visual.SetActive(true);

            if (_startListeningOnPoseSelect
                && (_voiceTriggerPoseIndex < 0 || poseNumber == _voiceTriggerPoseIndex)
                && _voiceManager != null)
            {
                _voiceManager.StartListening();
            }
        }

        bool TryPlaceVisual(int poseNumber, Transform target)
        {
            var hands = _poses[poseNumber].GetComponents<HandRef>();
            if (hands.Length > 0)
            {
                Vector3 sum = Vector3.zero;
                foreach (var hand in hands)
                {
                    hand.GetRootPose(out Pose wristPose);
                    Vector3 side = hand.Handedness == Handedness.Left ? wristPose.right : -wristPose.right;
                    sum += wristPose.position + side * 0.15f + Vector3.up * 0.02f;
                }

                target.position = sum / hands.Length;
                return true;
            }

            if (Hmd != null && Hmd.TryGetRootPose(out Pose hmdPose))
            {
                target.position = hmdPose.position + hmdPose.forward;
                target.forward = hmdPose.forward;
                return true;
            }

            return false;
        }

        public void HideVisuals(int poseNumber)
        {
            _poseActiveVisuals[poseNumber].SetActive(false);
        }
    }
}
