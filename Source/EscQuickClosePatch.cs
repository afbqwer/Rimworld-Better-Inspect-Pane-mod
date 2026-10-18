using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ASQBetterInspectPane;

/// <summary>
/// 杂项功能：Esc 快速关闭。
/// 原版中，当窗口的搜索输入框（QuickSearchWidget，即 Window.CommonSearchWidget）处于聚焦状态时，
/// KeyBindingDef.KeyDownEvent 会因 WindowStack.AnySearchWidgetFocused 返回 false，
/// 于是 Esc 不再触发关闭窗口，而由输入框自身处理（QuickSearchWidget.OnGUI 里 Unfocus + Use），
/// 表现为「按 Esc 只取消输入框聚焦、不关闭窗口」。
/// 开启本功能后，Esc 按下时无视输入框聚焦，直接走原版取消逻辑（WindowStack.Notify_PressedCancel）
/// 关闭当前聚焦的窗口；仅在原版确实会因输入框聚焦而跳过取消的场合接管，其余情况保持原版行为不变。
/// </summary>
[HarmonyPatch(typeof(WindowStack), nameof(WindowStack.HandleEventsHighPriority))]
public static class EscQuickClosePatch
{
    public static bool Prefix(WindowStack __instance)
    {
        if (!MyModTemplateSettings.enableEscQuickClose)
        {
            return true;
        }

        // 只处理 Esc 按下事件（其余帧事件直接放行，避免每帧读取输入焦点）。
        Event current = Event.current;
        if (current == null || current.type != EventType.KeyDown || current.keyCode != KeyCode.Escape)
        {
            return true;
        }

        // 仅在「输入框聚焦」这一原版会跳过取消的场景接管。
        if (!__instance.AnySearchWidgetFocused)
        {
            return true;
        }

        // 原版 Notify_PressedCancel 只在存在可取消窗口时才真正关闭窗口；这里做同样的判断，
        // 只有确实会关闭窗口时才消费 Esc，否则保持原版后续流程（搜索框取消聚焦）。
        if (!AnyCancelableWindow(__instance))
        {
            return true;
        }

        __instance.Notify_PressedCancel();
        current.Use();
        return true;
    }

    /// <summary>是否存在会被 Notify_PressedCancel 取消的窗口（口径与原版一致：可取消且有输入焦点）。</summary>
    private static bool AnyCancelableWindow(WindowStack stack)
    {
        IList<Window> windows = stack.Windows;
        for (int i = windows.Count - 1; i >= 0; i--)
        {
            Window window = windows[i];
            if ((window.closeOnCancel || window.forceCatchAcceptAndCancelEventEvenIfUnfocused) && stack.GetsInput(window))
            {
                return true;
            }
        }
        return false;
    }
}
