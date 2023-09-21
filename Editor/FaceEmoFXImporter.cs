using System;
using Suzuryg.FaceEmo.Components;
using Suzuryg.FaceEmo.Components.Data;
using Suzuryg.FaceEmo.Domain;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using FaceEmoMenu = Suzuryg.FaceEmo.Domain.Menu;
using FaceEmoAnimation = Suzuryg.FaceEmo.Domain.Animation;
using System.IO;

public class FaceEmoFXImporter : EditorWindow
{
    [MenuItem("FaceEmo/Import Expression Patterns from FX layer")]
    public static void ShowWindow() => CreateWindow<FaceEmoImporter>("FaceEmo FX Importer");

    private void OnGUI()
    {
        var so = new SerializedObject(this);

        EditorGUILayout.PropertyField(so.FindProperty(nameof(FX)));
        EditorGUILayout.PropertyField(so.FindProperty(nameof(FaceEmo)), new GUIContent("FaceEmo"));

        so.ApplyModifiedProperties();

        using (new EditorGUI.DisabledScope(FX == null || FaceEmo == null))
        {
            if (GUILayout.Button("Run"))
            {
                Run();
            }
        }

        EditorGUILayout.HelpBox("FaceEmoのウィンドウを開き直さないと反映されないかもしれません・・・・・・・・・・", MessageType.None);
    }

    private void Run()
    {
        var repo = FaceEmo.GetComponent<MenuRepositoryComponent>();
        var menu = repo.SerializableMenu.Load();

        var mode = menu.GetMode(menu.AddMode(FaceEmoMenu.RegisteredId)) as Mode;
        mode.DisplayName = "FX";
        var list = FX.layers
            .Reverse()
            .Select(x => x.stateMachine.anyStateTransitions)
            .SelectMany(x => x)
            .Select(CreateBranch)
            .Where(x => x != null);

        if (list.FirstOrDefault().Conditions.FirstOrDefault().Hand == Hand.Left)
        {
            list = list.OrderBy(x => x.Conditions.FirstOrDefault().Hand);
        }
        else
        {
            list = list.OrderByDescending(x => x.Conditions.FirstOrDefault().Hand);
        }

        foreach (var item in (list as IOrderedEnumerable<Branch>).ThenBy(x => x.Conditions.FirstOrDefault().HandGesture))
        {

            (mode.Branches as List<Branch>).Add(item);
        }

        repo.SerializableMenu.Save(menu, false);
    }

    private Branch CreateBranch(AnimatorStateTransition transition)
    {
        var dest = transition.destinationState;
        var br = new Branch();
        foreach (var x in transition.conditions)
        {
            Hand? hand = x.parameter == "GestureLeft" ? Hand.Left : x.parameter == "GestureRight" ? Hand.Right : default(Hand?);
            if (!hand.HasValue || x.threshold == 0)
                return null;

            br.AddCondition(new Condition(hand.Value, (HandGesture)(int)x.threshold, x.mode == AnimatorConditionMode.Equals ? ComparisonOperator.Equals : ComparisonOperator.NotEqual));
        }

        if (dest.motion is BlendTree blendTree)
        {
            if (blendTree.children.Length < 2)
                return null;

            if (blendTree.children[0].motion is AnimationClip first)
            {
                br.SetAnimation(first.ToFaceEmoAnimation(), BranchAnimationType.Base);
            }

            if (blendTree.children[1].motion is AnimationClip second)
            {
                br.SetAnimation(second.ToFaceEmoAnimation(), BranchAnimationType.Right);
            }
        }
        else if (dest.motion is AnimationClip animation)
        {
            if (dest.timeParameterActive)
            {
                AnimationClip first;
                AnimationClip second;
                if (_separatedAnimationCache.TryGetValue(animation, out var cache))
                {
                    (first, second) = cache;
                }
                else
                {
                    first = Instantiate(animation);
                    second = Instantiate(animation);

                    foreach (var x in AnimationUtility.GetCurveBindings(first).Select(x => (Bind: x, Curve: AnimationUtility.GetEditorCurve(first, x))).ToArray())
                    {
                        if (x.Curve.length >= 2)
                        {
                            x.Curve.RemoveKey(1);
                            AnimationUtility.SetEditorCurve(first, x.Bind, x.Curve);
                        }
                    }

                    foreach (var x in AnimationUtility.GetCurveBindings(second).Select(x => (Bind: x, Curve: AnimationUtility.GetEditorCurve(second, x))).ToArray())
                    {
                        if (x.Curve.length >= 2)
                        {
                            x.Curve.RemoveKey(0);
                            AnimationUtility.SetEditorCurve(second, x.Bind, x.Curve);
                        }
                    }

                    AssetDatabase.CreateAsset(first, $"{Path.GetDirectoryName(AssetDatabase.GetAssetPath(animation))}/{animation.name}_first_{GUID.Generate()}.anim");
                    AssetDatabase.CreateAsset(second, $"{Path.GetDirectoryName(AssetDatabase.GetAssetPath(animation))}/{animation.name}_second_{GUID.Generate()}.anim");

                    _separatedAnimationCache.Add(animation, (first, second));
                }
                br.SetAnimation(first.ToFaceEmoAnimation(), BranchAnimationType.Base);
                bool isLeft = br.Conditions.Any(x => x.Hand == Hand.Left);

                br.SetAnimation(second.ToFaceEmoAnimation(), isLeft ? BranchAnimationType.Left : BranchAnimationType.Right);
                br.IsRightTriggerUsed = !(br.IsLeftTriggerUsed = isLeft);
            }
            else
            {
                br.SetAnimation(animation.ToFaceEmoAnimation(), BranchAnimationType.Base);
            }
        }

        return br;
    }

    [SerializeField]
    private AnimatorController FX;

    [SerializeField]
    private FaceEmoLauncherComponent FaceEmo;

    private Dictionary<AnimationClip, (AnimationClip First, AnimationClip Second)> _separatedAnimationCache = new Dictionary<AnimationClip, (AnimationClip First, AnimationClip Second)>();
}

internal static class Extension
{
    public static FaceEmoAnimation ToFaceEmoAnimation(this AnimationClip animation)
    {
        return new FaceEmoAnimation(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(animation)));
    }
}