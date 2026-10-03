#pragma once
#include <windows.h>

namespace pydeck::setup {
struct UiWords {
    const wchar_t *title, *folder, *browse, *desktop, *startMenu, *dependencies, *license,
        *launch, *retry, *ready, *invalidFolder, *running, *sameVersion, *newerVersion,
        *repair, *upgrade, *installBusy, *installFailed, *launchFailed, *remove, *confirmRemove, *removed, *removing;
};
inline constexpr UiWords UiText[] = {
    {L"Install PyDeck", L"Installation folder", L"Browse...", L"Desktop shortcut", L"Start menu shortcut",
     L"Required components", L"License", L"Open PyDeck", L"Try again", L"Ready to install for your Windows account.",
     L"Choose an absolute folder path that your account can write to. Do not use a drive root or a file.",
     L"PyDeck is running from the installation folder. Finish your work and exit it, including its tray icon, then try again.",
     L"A different package with this version is installed. Use a newer MSI to upgrade, or remove the existing package first.",
     L"A newer PyDeck version is installed. This package cannot downgrade it.", L"Repair PyDeck", L"Upgrade PyDeck",
     L"Another installation is in progress. Wait for it to finish, then try again.",
     L"Installation could not finish. Open the logs for details; your options are kept for another attempt.",
     L"PyDeck could not be started. Run PyDeck.Launcher.exe from the installation folder.",
     L"Uninstall...", L"Uninstall PyDeck and its shortcuts? Python, virtual environments, shared runtimes and your app preferences will be kept.",
     L"PyDeck has been uninstalled. Your Python installations and app preferences have been kept.", L"Uninstalling PyDeck"},
    {L"安装 PyDeck", L"安装位置", L"浏览…", L"创建桌面快捷方式", L"添加到开始菜单",
     L"运行依赖", L"许可证", L"启动 PyDeck", L"重试", L"准备就绪，将为当前 Windows 用户安装。",
     L"请选择当前用户可写的绝对文件夹路径，不能使用磁盘根目录或文件。",
     L"安装目录中的 PyDeck 仍在运行。请先完成操作并退出应用（包括托盘中的实例），再重试。",
     L"已安装同版本的另一份安装包。请使用更高版本 MSI 升级，或先卸载现有版本。",
     L"已安装更高版本的 PyDeck，此安装包不能降级。", L"修复 PyDeck", L"升级 PyDeck",
     L"另一个安装程序正在运行。请等待它完成后重试。",
     L"安装未完成。请打开日志查看原因；已保留安装选项，可处理后重试。",
     L"无法启动 PyDeck。请进入安装目录，运行 PyDeck.Launcher.exe。",
     L"卸载…", L"是否卸载 PyDeck 及其快捷方式？Python、虚拟环境、共享运行时和应用偏好设置都会保留。",
     L"PyDeck 已卸载，Python 安装和应用偏好设置已保留。", L"正在卸载 PyDeck"},
    {L"安裝 PyDeck", L"安裝位置", L"瀏覽…", L"建立桌面捷徑", L"加入開始功能表",
     L"執行環境", L"授權條款", L"開啟 PyDeck", L"重試", L"準備就緒，將為目前 Windows 使用者安裝。",
     L"請選擇目前使用者可寫入的絕對資料夾路徑，不能使用磁碟根目錄或檔案。",
     L"安裝資料夾中的 PyDeck 仍在執行。請先完成工作並結束應用程式（包括通知區域中的執行個體），再重試。",
     L"已安裝同版本的另一個套件。請使用更新版本 MSI 升級，或先解除安裝現有版本。",
     L"已安裝較新版本的 PyDeck，此套件無法降級。", L"修復 PyDeck", L"升級 PyDeck",
     L"另一個安裝程式正在執行。請等待完成後重試。",
     L"安裝未完成。請開啟記錄查看原因；安裝選項已保留，可處理後重試。",
     L"無法開啟 PyDeck。請前往安裝資料夾，執行 PyDeck.Launcher.exe。",
     L"解除安裝…", L"解除安裝 PyDeck 及其捷徑？Python、虛擬環境、共用執行階段和應用程式偏好設定都會保留。",
     L"PyDeck 已解除安裝，Python 和應用程式偏好設定已保留。", L"正在解除安裝 PyDeck"},
    {L"PyDeck のインストール", L"インストール先", L"参照...", L"デスクトップにショートカットを作成", L"スタートメニューに追加",
     L"必要なランタイム", L"ライセンス", L"PyDeck を開く", L"再試行", L"現在の Windows ユーザーにインストールします。",
     L"書き込み可能な絶対フォルダーパスを選んでください。ドライブのルートやファイルは使用できません。",
     L"インストール先の PyDeck が実行中です。作業を完了して通知領域からも終了し、再試行してください。",
     L"同じバージョンの別パッケージが導入されています。新しい MSI を使うか、既存版を削除してください。",
     L"より新しい PyDeck が導入されています。このパッケージではダウングレードできません。",
     L"PyDeck を修復", L"PyDeck を更新", L"別のインストールが進行中です。完了後に再試行してください。",
     L"完了できませんでした。ログを確認して再試行してください。選択内容は保持されています。",
     L"PyDeck を起動できませんでした。インストール先で PyDeck.Launcher.exe を実行してください。",
     L"アンインストール...", L"PyDeck とショートカットを削除しますか？Python、仮想環境、共有ランタイム、アプリ設定は保持されます。",
     L"PyDeck を削除しました。Python とアプリ設定は保持されています。", L"PyDeck をアンインストール中"}
};
inline unsigned SetupLanguage(LANGID language) {
    if (PRIMARYLANGID(language) == LANG_JAPANESE) return 3;
    if (PRIMARYLANGID(language) == LANG_CHINESE)
        return SUBLANGID(language) == SUBLANG_CHINESE_TRADITIONAL ||
            SUBLANGID(language) == SUBLANG_CHINESE_HONGKONG || SUBLANGID(language) == SUBLANG_CHINESE_MACAU ? 2u : 1u;
    return 0;
}
}
