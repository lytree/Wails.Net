using WailsApplication = Wails.Net.Application.Application;
using Wails.Net.Application.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Managers;

namespace Wails.Net.Plugins.Dialog;

/// <summary>
/// 对话框插件，提供前端调用原生对话框的命令。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-dialog</c>。
/// 通过 <see cref="ICommandContext.Services"/> 从 DI 容器获取 <see cref="Application"/>，
/// 再访问 <see cref="Application.DialogManager"/> 委托到平台特定实现。
/// </summary>
public class DialogPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "dialog";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册对话框相关命令。
    /// 命令包括消息对话框（message/warning/error/question）和文件对话框（openFile/saveFile/openMultipleFiles）。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // R3-B 权限声明：所有 dialog 命令显式声明 capability。
        context.Permissions.RegisterPermissionSet("dialog:default", "dialog 默认权限集",
            "dialog:allow-open");
        context.Permissions.DeclarePermission("dialog:allow-open", "允许调用 dialog 插件命令");

        // 声明权限集
        context.Permissions.RegisterPermissionSet("dialog:default", "对话框默认权限集",
            "dialog:allow-open", "dialog:allow-save", "dialog:allow-message");
        context.Permissions.DeclarePermission("dialog:allow-open", "允许打开文件选择对话框");
        context.Permissions.DeclarePermission("dialog:allow-save", "允许打开文件保存对话框");
        context.Permissions.DeclarePermission("dialog:allow-message", "允许显示消息对话框");

        // 消息对话框：信息提示
        context.Commands.MapCommandAsync("dialog.message",
            (Func<ICommandContext, string, string, Task<int>>)(async (ctx, title, message) =>
        {
            var dialog = GetDialogManager(ctx);
            if (dialog is null)
            {
                return 0;
            }

            return await dialog.ShowMessageDialog(title, message, DialogStyle.Info, ["OK"]);
        }), CommandSecurity.Require("dialog:allow-open"));

        // 消息对话框：警告
        context.Commands.MapCommandAsync("dialog.warning",
            (Func<ICommandContext, string, string, Task<int>>)(async (ctx, title, message) =>
        {
            var dialog = GetDialogManager(ctx);
            if (dialog is null)
            {
                return 0;
            }

            return await dialog.ShowMessageDialog(title, message, DialogStyle.Warning, ["OK"]);
        }), CommandSecurity.Require("dialog:allow-open"));

        // 消息对话框：错误
        context.Commands.MapCommandAsync("dialog.error",
            (Func<ICommandContext, string, string, Task<int>>)(async (ctx, title, message) =>
        {
            var dialog = GetDialogManager(ctx);
            if (dialog is null)
            {
                return 0;
            }

            return await dialog.ShowMessageDialog(title, message, DialogStyle.Error, ["OK"]);
        }), CommandSecurity.Require("dialog:allow-open"));

        // 消息对话框：询问（支持自定义按钮标签；未提供时默认 Yes/No）
        context.Commands.MapCommandAsync("dialog.question",
            (Func<ICommandContext, string, string, string[]?, Task<int>>)(async (ctx, title, message, buttons) =>
        {
            var dialog = GetDialogManager(ctx);
            if (dialog is null)
            {
                return 0;
            }

            var labels = buttons is { Length: > 0 } ? buttons : DefaultQuestionButtons;
            var options = new MessageDialogOptions
            {
                Title = title,
                Message = message,
                Style = DialogStyle.Question,
                Buttons = BuildQuestionButtons(labels),
            };
            return await dialog.ShowMessageDialog(options);
        }), CommandSecurity.Require("dialog:allow-open"));

        // 打开文件对话框
        context.Commands.MapCommandAsync("dialog.openFile",
            (Func<ICommandContext, string?, string?, string[]?, Task<string?>>)(async (ctx, title, directory, filters) =>
        {
            var dialog = GetDialogManager(ctx);
            if (dialog is null)
            {
                return null;
            }

            var options = new OpenFileDialogOptions
            {
                Title = title,
                Directory = directory,
                Filters = filters,
                AllowFiles = true
            };
            return await dialog.OpenFileDialog(options);
        }), CommandSecurity.Require("dialog:allow-open"));

        // 保存文件对话框
        context.Commands.MapCommandAsync("dialog.saveFile",
            (Func<ICommandContext, string?, string?, string?, string[]?, Task<string?>>)(async (ctx, title, directory, filename, filters) =>
        {
            var dialog = GetDialogManager(ctx);
            if (dialog is null)
            {
                return null;
            }

            var options = new SaveFileDialogOptions
            {
                Title = title,
                Directory = directory,
                Filename = filename,
                Filters = filters,
                CreateDirectories = true
            };
            return await dialog.SaveFileDialog(options);
        }), CommandSecurity.Require("dialog:allow-open"));

        // 打开多文件选择对话框
        context.Commands.MapCommandAsync("dialog.openMultipleFiles",
            (Func<ICommandContext, string?, string?, string[]?, Task<string[]?>>)(async (ctx, title, directory, filters) =>
        {
            var dialog = GetDialogManager(ctx);
            if (dialog is null)
            {
                return null;
            }

            var options = new OpenFileDialogOptions
            {
                Title = title,
                Directory = directory,
                Filters = filters,
                AllowFiles = true,
                CanChooseMultiple = true
            };
            return await dialog.OpenMultipleFilesDialog(options);
        }), CommandSecurity.Require("dialog:allow-open"));
    }

    /// <summary>
    /// 询问对话框的默认按钮标签。
    /// </summary>
    private static readonly string[] DefaultQuestionButtons = ["Yes", "No"];

    /// <summary>
    /// 将按钮标签转换为按钮定义。
    /// 按钮数 >= 2 时把最后一个按钮标记为取消按钮，使 Esc/关闭对话框回退到该按钮
    /// （对应 Go TaskDialog 的 TDF_ALLOW_DIALOG_CANCELLATION 语义）。
    /// </summary>
    /// <param name="labels">按钮标签数组。</param>
    /// <returns>按钮定义数组。</returns>
    private static MessageDialogButton[] BuildQuestionButtons(string[] labels)
    {
        var buttons = new MessageDialogButton[labels.Length];
        for (var i = 0; i < labels.Length; i++)
        {
            buttons[i] = new MessageDialogButton
            {
                Label = labels[i],
                IsCancel = labels.Length >= 2 && i == labels.Length - 1,
            };
        }

        return buttons;
    }

    /// <summary>
    /// 从命令上下文中获取对话框管理器实例。
    /// </summary>
    /// <param name="ctx">命令上下文。</param>
    /// <returns>对话框管理器实例，若未注册则返回 null。</returns>
    private static IDialogManager? GetDialogManager(ICommandContext ctx)
    {
        var app = ctx.Services.GetService<WailsApplication>();
        return app?.DialogManager;
    }
}
