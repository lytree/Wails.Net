using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Managers;

namespace Wails.Net.Application.Plugins.BuiltIn;

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
        // 消息对话框：信息提示
        context.Commands.MapCommand("dialog.message",
            (Func<ICommandContext, string, string, Task<int>>)(async (ctx, title, message) =>
        {
            var dialog = GetDialogManager(ctx);
            if (dialog is null)
            {
                return 0;
            }

            return await dialog.ShowMessageDialog(title, message, DialogStyle.Info, ["OK"]);
        }));

        // 消息对话框：警告
        context.Commands.MapCommand("dialog.warning",
            (Func<ICommandContext, string, string, Task<int>>)(async (ctx, title, message) =>
        {
            var dialog = GetDialogManager(ctx);
            if (dialog is null)
            {
                return 0;
            }

            return await dialog.ShowMessageDialog(title, message, DialogStyle.Warning, ["OK"]);
        }));

        // 消息对话框：错误
        context.Commands.MapCommand("dialog.error",
            (Func<ICommandContext, string, string, Task<int>>)(async (ctx, title, message) =>
        {
            var dialog = GetDialogManager(ctx);
            if (dialog is null)
            {
                return 0;
            }

            return await dialog.ShowMessageDialog(title, message, DialogStyle.Error, ["OK"]);
        }));

        // 消息对话框：询问
        context.Commands.MapCommand("dialog.question",
            (Func<ICommandContext, string, string, Task<int>>)(async (ctx, title, message) =>
        {
            var dialog = GetDialogManager(ctx);
            if (dialog is null)
            {
                return 0;
            }

            return await dialog.ShowMessageDialog(title, message, DialogStyle.Question, ["Yes", "No"]);
        }));

        // 打开文件对话框
        context.Commands.MapCommand("dialog.openFile",
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
        }));

        // 保存文件对话框
        context.Commands.MapCommand("dialog.saveFile",
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
        }));

        // 打开多文件选择对话框
        context.Commands.MapCommand("dialog.openMultipleFiles",
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
        }));
    }

    /// <summary>
    /// 从命令上下文中获取对话框管理器实例。
    /// </summary>
    /// <param name="ctx">命令上下文。</param>
    /// <returns>对话框管理器实例，若未注册则返回 null。</returns>
    private static IDialogManager? GetDialogManager(ICommandContext ctx)
    {
        var app = ctx.Services.GetService<Application>();
        return app?.DialogManager;
    }
}
