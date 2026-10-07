using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;

namespace ASQBetterInspectPane;

/// <summary>
/// 记忆已打开观察面板标签页：原版切换目标种类后会把标签页关闭，再次选中同种目标时不会自动打开。
/// 本补丁在 InspectPaneUtility.UpdateTabs 前记录各目标种类最近打开的标签页，
/// 当切回同种目标且之前打开的标签页仍可打开时自动打开（默认关闭，见设置 rememberOpenTab）。
/// 记忆按「最近 N 个目标种类」保存（LRU，容量 N 由设置 rememberOpenTabCapacity 决定，范围 2~10）。
/// 目标种类由该目标可打开的标签页类型集合决定；地图与世界地图观察面板共用同一 UpdateTabs 钩子。
/// </summary>
[HarmonyPatch(typeof(InspectPaneUtility), nameof(InspectPaneUtility.UpdateTabs))]
public static class InspectTabMemoryPatch
{
    // 目标种类键 -> 该种类最近打开的标签页类型。
    private static readonly Dictionary<string, Type> rememberedTab = new Dictionary<string, Type>();
    // LRU 顺序（最近优先），元素数量不超过设置容量。
    private static readonly List<string> lruOrder = new List<string>();

    // 上一帧（UpdateTabs）观察到的目标种类键与打开的标签页类型（"" / null 表示无）。
    private static string lastTypeKey = "";
    private static Type? lastOpenTabType;

    // 手动关闭后抑制自动打开的种类的键：用户在某种类上主动关闭标签页后，
    // 停留同类型期间不再自动重开；切换到其它类型后清除。
    private static string suppressedTypeKey = "";

    public static bool Prefix(IInspectPane pane)
    {
        if (!MyModTemplateSettings.rememberOpenTab)
        {
            return true;
        }

        // 枚举当前目标可打开的标签页，并计算「目标种类键」（可打开标签页类型的有序集合）。
        List<InspectTabBase>? tabs = null;
        if (pane.CurTabs != null)
        {
            tabs = new List<InspectTabBase>(pane.CurTabs);
        }
        string typeKey = tabs is { Count: > 0 } ? BuildTypeKey(tabs) : "";
        Type openTabType = pane.OpenTabType;

        if (openTabType != null)
        {
            // (a) 当前有标签页打开。
            InspectTabBase? openTab = FindTab(tabs, openTabType);
            if (openTab != null)
            {
                // 仍属于当前种类：记忆并刷新 LRU。
                Remember(typeKey, openTabType);
            }
            else if (lastTypeKey.Length > 0)
            {
                // 刚切换种类、原版即将因标签页不在新种类中而关闭：记到旧种类下。
                Remember(lastTypeKey, openTabType);
            }
        }
        else
        {
            // (b) 当前没有标签页打开。
            // 手动关闭检测：同种类下上一帧有打开、本帧无 → 抑制自动打开。
            if (typeKey.Length > 0 && typeKey == lastTypeKey && lastOpenTabType != null)
            {
                suppressedTypeKey = typeKey;
            }
            // 类型切换：清除抑制，并把新种类记入 LRU（占用一个记忆槽，超出容量时淘汰最旧）。
            if (typeKey.Length > 0 && typeKey != lastTypeKey)
            {
                suppressedTypeKey = "";
                Touch(typeKey);
            }
            // 自动打开：切回同种目标时，若记忆中的标签页仍可打开则自动打开。
            if (typeKey.Length > 0
                && suppressedTypeKey != typeKey
                && rememberedTab.TryGetValue(typeKey, out Type remembered)
                && FindOpenableTab(tabs, remembered, out InspectTabBase? tab))
            {
                tab!.OnOpen();
                pane.OpenTabType = remembered;
                Touch(typeKey);
            }
        }

        lastTypeKey = typeKey;
        lastOpenTabType = pane.OpenTabType;
        return true;
    }

    /// <summary>记忆某种类最近打开的标签页，并刷新 LRU。</summary>
    private static void Remember(string typeKey, Type tabType)
    {
        if (typeKey.Length == 0 || tabType == null)
        {
            return;
        }
        rememberedTab[typeKey] = tabType;
        Touch(typeKey);
    }

    /// <summary>把某种类标记为最近使用；超出设置容量时淘汰最旧的种类及其记忆。</summary>
    private static void Touch(string typeKey)
    {
        int capacity = Mathf.Clamp(MyModTemplateSettings.RememberOpenTabCapacity, 2, 10);
        lruOrder.Remove(typeKey);
        lruOrder.Insert(0, typeKey);
        while (lruOrder.Count > capacity)
        {
            string evicted = lruOrder[lruOrder.Count - 1];
            lruOrder.RemoveAt(lruOrder.Count - 1);
            rememberedTab.Remove(evicted);
        }
    }

    /// <summary>目标种类键：当前目标可打开标签页类型的有序拼接（种类由可打开的标签页决定）。</summary>
    private static string BuildTypeKey(List<InspectTabBase> tabs)
    {
        List<string> names = new List<string>(tabs.Count);
        for (int i = 0; i < tabs.Count; i++)
        {
            names.Add(tabs[i].GetType().FullName ?? tabs[i].GetType().Name);
        }
        names.Sort(StringComparer.Ordinal);
        return string.Join(",", names);
    }

    private static InspectTabBase? FindTab(List<InspectTabBase>? tabs, Type type)
    {
        if (tabs == null || type == null)
        {
            return null;
        }
        for (int i = 0; i < tabs.Count; i++)
        {
            if (tabs[i].GetType() == type)
            {
                return tabs[i];
            }
        }
        return null;
    }

    /// <summary>在可打开标签页中找到记忆类型且当前可见的实例（可见才能打开）。</summary>
    private static bool FindOpenableTab(List<InspectTabBase>? tabs, Type type, out InspectTabBase? tab)
    {
        tab = null;
        if (tabs == null || type == null)
        {
            return false;
        }
        for (int i = 0; i < tabs.Count; i++)
        {
            InspectTabBase t = tabs[i];
            if (t.GetType() == type && t.IsVisible)
            {
                tab = t;
                return true;
            }
        }
        return false;
    }
}
