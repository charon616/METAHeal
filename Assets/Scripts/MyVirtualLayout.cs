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

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Curved UI layout: mirrors child RectTransforms into a parent layout and applies cylindrical yaw/spacing.
/// Used for Quest-style curved panels where flat layout children are repositioned onto a virtual cylinder.
/// </summary>
[ExecuteAlways]
public class MyVirtualLayout : UIBehaviour
{
    public float animationSpeed;
    [SerializeField] private RectTransform _layoutParent;
    [Header("Cylindrical Layout")]
    [SerializeField] private bool _useCylindricalLayout = true;
    [SerializeField, Min(1f)] private float _cylinderRadius = 1400f;
    [SerializeField, Range(0f, 45f)] private float _maxYawAngle = 10f;

    private List<RectTransform> _rectChildren;
    private List<RectTransform> _virtualLayoutChildren;

    protected override void OnEnable()
    {
        if (_layoutParent == null) return;
        var layoutChildren = _layoutParent.gameObject.GetComponentsInChildren<RectTransform>();
        for (int i = 1; i < layoutChildren.Length; i++)
        {
            var child = layoutChildren[i];
            if (Application.isPlaying)
            {
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }

        var children = gameObject.GetComponentsInChildren<RectTransform>();
        _rectChildren = new List<RectTransform>();
        _virtualLayoutChildren = new List<RectTransform>();
        for (int i = 1; i < children.Length; i++)
        {
            var child = children[i];
            if (child.parent != (RectTransform)transform) continue;
            _rectChildren.Add(child);
            ResetChildTransform(child);


            var virtualTransform = new GameObject();
            virtualTransform.hideFlags = HideFlags.HideAndDontSave;
            virtualTransform.name = child.name;
            virtualTransform.AddComponent<RectTransform>();
            var virtualChild = (RectTransform)virtualTransform.transform;
            virtualChild.SetParent(_layoutParent, false);
            ResetChildTransform(virtualChild);

            _virtualLayoutChildren.Add(virtualChild);
        }

        _layoutParent.ForceUpdateRectTransforms();
    }

    private void ResetChildTransform(RectTransform child)
    {
        child.localPosition = Vector3.zero;
        child.anchoredPosition = Vector2.zero;
        child.localScale = Vector3.one;
        child.localRotation = Quaternion.identity;
        child.anchorMin = Vector2.zero;
        child.anchorMax = Vector2.zero;
        child.pivot = new Vector2(0.5f, 0.5f);
    }

    protected override void OnDisable()
    {
        foreach (var child in _virtualLayoutChildren)
        {
            if (Application.isPlaying)
            {
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }
    }

    private void LateUpdate()
    {
        if (_layoutParent == null) return;
        var layoutTransform = (RectTransform)transform;
        layoutTransform.anchoredPosition = _layoutParent.anchoredPosition;

        float maxAbsX = 0f;
        if (_useCylindricalLayout)
        {
            for (int i = 0; i < _virtualLayoutChildren.Count; i++)
            {
                maxAbsX = Mathf.Max(maxAbsX, Mathf.Abs(_virtualLayoutChildren[i].anchoredPosition.x));
            }
        }

        for (int i = 0; i < _virtualLayoutChildren.Count; i++)
        {
            var rectChild = _rectChildren[i];
            var virtualChild = _virtualLayoutChildren[i];

            var targetAnchoredPos = virtualChild.anchoredPosition;
            if (!Application.isPlaying)
            {
                targetAnchoredPos += _layoutParent.anchoredPosition;
            }

            var targetLocalPosition = new Vector3(targetAnchoredPos.x, targetAnchoredPos.y, 0f);
            var targetRotation = Quaternion.identity;

            if (_useCylindricalLayout && maxAbsX > 0.001f)
            {
                var normalizedX = Mathf.Clamp(targetAnchoredPos.x / maxAbsX, -1f, 1f);
                var yawAngle = normalizedX * _maxYawAngle;
                var yawRadians = yawAngle * Mathf.Deg2Rad;

                targetLocalPosition.x = Mathf.Sin(yawRadians) * _cylinderRadius;
                targetLocalPosition.z = _cylinderRadius * (1f - Mathf.Cos(yawRadians));
                targetRotation = Quaternion.Euler(0f, -yawAngle, 0f);
            }

            if (Application.isPlaying)
            {
                rectChild.localPosition = Vector3.Lerp(rectChild.localPosition, targetLocalPosition, animationSpeed * Time.deltaTime);
                rectChild.sizeDelta = Vector2.Lerp(rectChild.sizeDelta, virtualChild.sizeDelta, animationSpeed * Time.deltaTime);
                rectChild.localRotation = Quaternion.Slerp(rectChild.localRotation, targetRotation, animationSpeed * Time.deltaTime);
            }
            else
            {
                rectChild.localPosition = targetLocalPosition;
                rectChild.sizeDelta = virtualChild.sizeDelta;
                rectChild.localRotation = targetRotation;
            }
        }
    }

    #region Inject
    public void InjectAllVirtualLayoutElement(RectTransform layoutParent)
    {
        _layoutParent = layoutParent;
    }
    #endregion
}
