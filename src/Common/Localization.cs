using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;


namespace AutoMacro
{
    internal static class L
    {
        internal static string Current = "ko";
        [DllImport("kernel32.dll")] static extern ushort GetUserDefaultUILanguage();
        internal static string WindowsDefault()
        {
            try { return Supported(CultureInfo.GetCultureInfo(GetUserDefaultUILanguage()).TwoLetterISOLanguageName); }
            catch { return Supported(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName); }
        }
        internal static string Supported(string language) { return language == "ko" || language == "ja" ? language : "en"; }
        internal static void Initialize(string path)
        {
            Current = WindowsDefault();
            try
            {
                if (DataStore.Exists(path))
                {
                    DataNode root = SettingsStore.Read(path);
                    string saved = (string)root.Element("Language");
                    if (saved == "ko" || saved == "en" || saved == "ja") { Current = saved;  }
                }
            }
            catch { /* Use the Windows display language when saved preferences cannot be read. */ }
        }
        internal static void Save(string path, string language)
        {
            SettingsStore.Update(path, new DataNode("Language", Supported(language)));
        }
        internal static string T(string korean) { return In(korean, Current); }
        static string In(string korean, string language)
        {
            string[] value;
            return language == "ko" || !Messages.TryGetValue(korean, out value) ? korean : value[language == "ja" ? 1 : 0];
        }
        internal static string Convert(string text, string previous)
        {
            if (String.IsNullOrEmpty(text)) return text;
            foreach (KeyValuePair<string, string[]> pair in Messages)
                if (text == In(pair.Key, previous)) return T(pair.Key);
            // Replace known fragments in status messages without re-translating replacements.
            List<string> keys = new List<string>(Messages.Keys);
            keys.Sort(delegate(string a, string b) { return In(b, previous).Length.CompareTo(In(a, previous).Length); });
            List<string> values = new List<string>();
            foreach (string key in keys)
            {
                string old = In(key, previous);
                if (old.Length == 0 || text.IndexOf(old, StringComparison.Ordinal) < 0) continue;
                string token = "\u0001" + values.Count + "\u0002";
                text = text.Replace(old, token); values.Add(T(key));
            }
            for (int i = 0; i < values.Count; i++) text = text.Replace("\u0001" + i + "\u0002", values[i]);
            return text;
        }
        internal static void Apply(Control control, string previous)
        {
            if (!(control is TextBox) && !(control is ComboBox)) control.Text = Convert(control.Text, previous);
            ComboBox combo = control as ComboBox;
            if (combo != null)
            {
                int selected = combo.SelectedIndex;
                for (int i = 0; i < combo.Items.Count; i++) combo.Items[i] = Convert(combo.Items[i].ToString(), previous);
                combo.SelectedIndex = selected;
            }
            ListView list = control as ListView;
            if (list != null) foreach (ColumnHeader column in list.Columns) column.Text = Convert(column.Text, previous);
            foreach (Control child in control.Controls) Apply(child, previous);
        }
        internal static DialogResult Confirm(IWin32Window owner, string message, bool allowCancel)
        {
            using (Form dialog = new Form { Text = "Auto Macro", ClientSize = new System.Drawing.Size(480, 220), BackColor = Theme.Background, ForeColor = Theme.Ink,
                Font = new System.Drawing.Font("맑은 고딕", 10), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false })
            {
                Label text = new Label { Text = message }; text.SetBounds(24, 24, 432, 122); dialog.Controls.Add(text);
                string[] names = allowCancel ? new string[] { T("예"), T("아니요"), T("취소") } : new string[] { T("예"), T("아니요") };
                DialogResult[] choices = { DialogResult.Yes, DialogResult.No, DialogResult.Cancel };
                for (int i = 0; i < names.Length; i++)
                {
                    Button button = new ModernButton { Text = names[i], DialogResult = choices[i] };
                    int width = allowCancel ? 136 : 210; button.SetBounds(24 + i * (width + 12), 160, width, 36);
                    Theme.Button(button, i == names.Length - 1); dialog.Controls.Add(button);
                    if (i == names.Length - 1) { dialog.AcceptButton = button; dialog.CancelButton = button; }
                }
                return dialog.ShowDialog(owner);
            }
        }
        internal static readonly Dictionary<string, string[]> Messages = new Dictionary<string, string[]>
        {
            { "업데이트 완료", new string[] { "Update complete", "更新完了" } },
            { "업데이트 내용을 불러오고 있습니다.", new string[] { "Loading release notes...", "更新内容を読み込んでいます。" } },
            { "업데이트가 완료되었습니다. 변경 내용을 확인하세요.", new string[] { "The update is complete. Review the changes below.", "更新が完了しました。変更内容をご確認ください。" } },
            { "이 버전에는 등록된 업데이트 내용이 없습니다.", new string[] { "No release notes were provided for this version.", "このバージョンには更新内容が登録されていません。" } },
            { "업데이트 내용만 불러오지 못했습니다. 프로그램은 정상적으로 사용할 수 있습니다.", new string[] { "Could not load release notes. The application is ready to use.", "更新内容を読み込めませんでした。プログラムは通常どおり使用できます。" } },
            { "릴리스 페이지", new string[] { "Release page", "リリースページ" } },
            { "릴리스 페이지를 열지 못했습니다.", new string[] { "Could not open the release page.", "リリースページを開けませんでした。" } },
            { "다시 시도", new string[] { "Retry", "再試行" } },
            { "닫기", new string[] { "Close", "閉じる" } },
            { "재생 매크로: {0}", new string[] { "Playing macro: {0}", "再生マクロ: {0}" } },
            { "재생 속도 {0}배", new string[] { "Playback speed {0}×", "再生速度 {0}倍" } },
            { "검색", new string[] { "Search", "検索" } },
            { "저장 순서", new string[] { "Saved order", "保存順" } },
            { "이름순", new string[] { "Name", "名前順" } },
            { "최근 수정순", new string[] { "Recently modified", "更新順" } },
            { "좌표 기준 설정", new string[] { "Coordinate settings", "座標基準設定" } },
            { "화면 전체 기준", new string[] { "Entire screen", "画面全体基準" } },
            { "선택한 창 내부 기준", new string[] { "Inside the selected window", "選択したウィンドウ内基準" } },
            { "대상 창 선택", new string[] { "Select target window", "対象ウィンドウの選択" } },
            { "선택", new string[] { "Select", "選択" } },
            { "새로 고침", new string[] { "Refresh", "更新" } },
            { "기준 창 선택", new string[] { "Select reference", "基準ウィンドウ選択" } },
            { "기준 창을 선택했습니다.", new string[] { "Reference window selected.", "基準ウィンドウを選択しました。" } },
            { "좌표 기준을 저장했습니다.", new string[] { "Coordinate settings saved.", "座標基準を保存しました。" } },
            { "먼저 녹화 당시의 기준 창을 선택하세요.", new string[] { "First select the reference window at its recorded position.", "まず録画時の位置にある基準ウィンドウを選択してください。" } },
            { "녹화 당시 창 기준이 기록되어 있습니다.", new string[] { "The reference window position was captured during recording.", "録画時のウィンドウ基準は記録されています。" } },
            { "기존 녹화는 녹화 당시 창을 같은 위치에 둔 후 기준 창을 선택하세요.", new string[] { "For older recordings, place the original window where it was recorded, then select it as the reference.", "以前の録画は、録画時と同じ位置に元のウィンドウを置き、基準として選択してください。" } },
            { "실행할 때 대상 창을 선택합니다. 창을 이동해도 창 내부의 같은 위치에 입력합니다. 다른 창으로 전환하면 중단합니다.", new string[] { "Select a target at playback. Input follows its client area when moved. Switching windows stops playback.", "実行時に対象を選択します。移動後もウィンドウ内の同じ位置に入力します。別のウィンドウに切り替えると停止します。" } },
            { "창 내부 기준 재생은 대상 창을 활성화합니다. 백그라운드 입력 기능이 아닙니다.", new string[] { "Window-relative playback activates the target window. It does not provide background input.", "ウィンドウ内基準の再生は対象をアクティブにします。バックグラウンド入力機能ではありません。" } },
            { "사용할 수 있는 대상 창을 선택하세요.", new string[] { "Select an available target window.", "使用できる対象ウィンドウを選択してください。" } },
            { "대상 창을 활성화하지 못했습니다. 대상 창을 확인하세요.", new string[] { "Could not activate the target. Check the window.", "対象をアクティブにできませんでした。ウィンドウを確認してください。" } },
            { "대상 창을 사용할 수 없어 재생을 중단했습니다.", new string[] { "Playback stopped because the target window is unavailable.", "対象ウィンドウを使用できないため再生を停止しました。" } },
            { "대상 창이 닫히거나 최소화되었거나 다른 창으로 전환되어 재생을 중단했습니다.", new string[] { "Playback stopped: the target closed, was minimized, or lost focus.", "対象が閉じられた、最小化された、または別のウィンドウに切り替わったため再生を停止しました。" } },
            { "입력 위치가 대상 창 밖이어서 재생을 중단했습니다.", new string[] { "Playback stopped because an input position is outside the target window.", "入力位置が対象ウィンドウの外にあるため再生を停止しました。" } },
            { "실행 단축키 설정", new string[] { "Set run shortcut", "実行ショートカット設定" } },
            { "실행 단축키 해제", new string[] { "Clear run shortcut", "実行ショートカット解除" } },
            { "다른 매크로의 실행 단축키와 중복됩니다.", new string[] { "Already assigned to another macro.", "別のマクロの実行ショートカットと重複しています。" } },
            { "시작·녹화·긴급 정지 단축키와 중복됩니다.", new string[] { "Conflicts with a play, record or emergency stop shortcut.", "実行・録画・緊急停止ショートカットと重複しています。" } },
            { "Clicker 입력 키 또는 단축키와 중복됩니다.", new string[] { "Conflicts with a Clicker input key or shortcut.", "Clickerの入力キーまたはショートカットと重複しています。" } },
            { "이 매크로에 기록된 입력 키와 중복됩니다.", new string[] { "Conflicts with an input key recorded in this macro.", "このマクロに記録された入力キーと重複しています。" } },
            { "재생 속도 (배)", new string[] { "Playback speed (×)", "再生速度 (倍)" } },
            { "재생 속도는 동작 간격에 적용됩니다. 반복 사이 대기 시간은 유지됩니다.", new string[] { "Speed changes action timing. The delay between repeats stays unchanged.", "速度は動作間隔に適用されます。繰り返し間の待機時間は変わりません。" } },
            { "예상 남은 시간 {0} · 현재 동작: {1}", new string[] { "Est. remaining {0} · Current action: {1}", "残り時間の目安 {0} · 現在の動作: {1}" } },
            { "반복 대기", new string[] { "Waiting for repeat", "繰り返し待機" } },
            { "시작 대기", new string[] { "Starting", "開始待機" } },
            { "처리 중", new string[] { "Processing", "処理中" } },
            { "매크로 파일을 처리하고 있습니다.", new string[] { "Processing the macro file.", "マクロファイルを処理しています。" } },
            { "저장 한도를 초과했습니다. 최대 1,000개 매크로, 전체 500,000개 동작까지 저장할 수 있습니다.", new string[] { "Storage limit exceeded: up to 1,000 macros and 500,000 actions in total.", "保存上限を超えました。マクロ1,000件、動作合計500,000件まで保存できます。" } },
            { "정지 중…", new string[] { "Stopping…", "停止中…" } },
            { "누르기 없이 떼기 동작이 있습니다. 입력 짝을 맞춘 후 저장하세요.", new string[] { "A release has no matching press. Fix the input pairs before saving.", "押す操作のない離す操作があります。入力の組を修正してから保存してください。" } },
            { "떼기 없는 누르기 동작이 있습니다. 입력 짝을 맞춘 후 저장하세요.", new string[] { "A press has no matching release. Fix the input pairs before saving.", "離す操作のない押す操作があります。入力の組を修正してから保存してください。" } },
            { "업데이트", new string[] { "Updates", "アップデート" } },
            { "새 버전 감지", new string[] { "New version available", "新バージョンを検出" } },
            { "나중에", new string[] { "Later", "後で" } },
            { "새로운 최신 버전이 감지되었습니다. 지금 업데이트할 수 있습니다.", new string[] { "A newer version was detected. You can update now.", "新しいバージョンが検出されました。今すぐ更新できます。" } },
            { "업데이트 확인", new string[] { "Check for updates", "更新を確認" } },
            { "최신 버전으로 업데이트", new string[] { "Update to latest version", "最新バージョンに更新" } },
            { "최신 버전: 확인 전", new string[] { "Latest version: not checked", "最新バージョン: 未確認" } },
            { "최신 버전: 확인 중", new string[] { "Latest version: checking", "最新バージョン: 確認中" } },
            { "최신 버전: 확인 실패", new string[] { "Latest version: check failed", "最新バージョン: 確認失敗" } },
            { "최신 버전: {0}", new string[] { "Latest version: {0}", "最新バージョン: {0}" } },
            { "최신 버전: 등록된 릴리스가 없습니다.", new string[] { "Latest version: no release published", "最新バージョン: リリース未公開" } },
            { "업데이트 확인을 눌러 최신 버전을 확인하세요.", new string[] { "Select Check for updates to check the latest version.", "「更新を確認」で最新バージョンを確認してください。" } },
            { "최신 버전을 확인하고 있습니다.", new string[] { "Checking for the latest version.", "最新バージョンを確認しています。" } },
            { "업데이트 파일을 다운로드하고 검증하고 있습니다.", new string[] { "Downloading and verifying the update.", "更新ファイルをダウンロードし、検証しています。" } },
            { "현재 최신 버전을 사용 중입니다.", new string[] { "You are using the latest version.", "最新バージョンを使用しています。" } },
            { "아직 공개된 정식 릴리스가 없습니다. 나중에 다시 확인하세요.", new string[] { "No stable release has been published yet. Check again later.", "正式リリースはまだ公開されていません。後でもう一度確認してください。" } },
            { "새 버전이 있지만 업데이트 파일이 아직 등록되지 않았습니다.", new string[] { "A new version exists, but its update file has not been uploaded yet.", "新バージョンがありますが、更新ファイルはまだ登録されていません。" } },
            { "새 버전을 사용할 수 있습니다. 업데이트 후 자동으로 다시 시작합니다.", new string[] { "A new version is available. The app will restart after updating.", "新バージョンがあります。更新後、自動的に再起動します。" } },
            { "최신 버전으로 업데이트하고 프로그램을 다시 시작할까요? 설정과 저장된 매크로는 유지됩니다.", new string[] { "Update to the latest version and restart? Settings and saved macros will be kept.", "最新バージョンに更新して再起動しますか？設定と保存済みマクロは維持されます。" } },
            { "업데이트 파일이 올바르지 않습니다. 기존 프로그램은 유지됩니다.", new string[] { "The update file is invalid. The existing app will be kept.", "更新ファイルが正しくありません。既存のプログラムは維持されます。" } },
            { "업데이트 서버에 연결하지 못했습니다. 인터넷 연결을 확인하고 다시 시도하세요.", new string[] { "Could not connect to the update server. Check your connection and try again.", "更新サーバーに接続できません。接続を確認して再試行してください。" } },
            { "업데이트를 준비하지 못했습니다. 폴더 권한과 여유 공간을 확인하세요.", new string[] { "Could not prepare the update. Check folder permissions and free space.", "更新を準備できません。フォルダーの権限と空き容量を確認してください。" } },
            { "업데이트 설치를 시작하지 못했습니다. 기존 프로그램을 다시 실행해주세요.", new string[] { "Could not start the update installer. Please reopen the existing app.", "更新を開始できません。既存のプログラムをもう一度起動してください。" } },
            { "프로그램이 종료되지 않아 업데이트를 취소했습니다.", new string[] { "The app did not close, so the update was cancelled.", "プログラムが終了しなかったため、更新をキャンセルしました。" } },
            { "업데이트 복구에 실패했습니다. 기존 실행 파일이 다음 위치에 보존되어 있습니다.", new string[] { "Update recovery failed. The previous executable is preserved at:", "更新の復旧に失敗しました。以前の実行ファイルは次の場所に保存されています。" } },
            { "업데이트에 실패하여 기존 버전으로 복구했습니다. 프로그램을 다시 실행해주세요.", new string[] { "The update failed and the previous version was restored. Please reopen the app.", "更新に失敗したため、以前のバージョンに復元しました。もう一度起動してください。" } },
            { "예", new string[] { "Yes", "はい" } },
            { "아니요", new string[] { "No", "いいえ" } },
            { "언어 설정", new string[] { "Language settings", "言語設定" } },
            { "기타 설정", new string[] { "Other settings", "その他の設定" } },
            { "언어", new string[] { "Language", "言語" } },
            { "언어 설정을 저장하지 못했습니다. 폴더 권한을 확인하세요.", new string[] { "Could not save the language. Check folder permissions.", "言語設定を保存できません。フォルダーの権限を確認してください。" } },
            { "매크로 프로그램 사용의 책임은 온전히 사용자 본인에게 있습니다.", new string[] { "You are solely responsible for your use of this macro program.", "このマクロプログラムの使用に関する責任は、すべて利用者本人にあります。" } },
            { "안티치트, 보안 프로그램 등이 가동중인 곳에서는 사용을 자제해주세요.\n이 프로그램은 어떠한 우회 기능도 제공하지 않습니다.", new string[] { "Please refrain from using this program while anti-cheat or security software is active.\nThis program does not provide any bypass features.", "アンチチートやセキュリティソフトの動作中は使用をお控えください。\nこのプログラムは回避機能を一切提供しません。" } },
            { "Auto Macro · 사용 안내", new string[] { "Auto Macro · Usage notice", "Auto Macro · 使用上のご案内" } },
            { "사용 전 안내", new string[] { "Before you begin", "使用前のご案内" } },
            { "이해했습니다.", new string[] { "I understand.", "理解しました。" } },
            { "확인 여부를 저장하지 못했습니다. 쓰기 가능한 폴더에서 실행해주세요.", new string[] { "Could not save your acknowledgement. Run from a writable folder.", "確認情報を保存できません。書き込み可能なフォルダーから実行してください。" } },
            { "키보드 키 또는 마우스 4·5번 버튼을 눌렀다가 떼세요.", new string[] { "Press and release a keyboard key or mouse button 4 or 5.", "キーボードのキー、またはマウスの4・5ボタンを押して離してください。" } },
            { "지정할 키를 한 번 눌렀다가 떼세요.", new string[] { "Press and release the key you want to assign.", "設定するキーを押して離してください。" } },
            { "\n다른 기능에 지정된 키는 사용할 수 없습니다.\n취소하려면 창의 X 버튼을 누르세요.", new string[] { "\nKeys assigned to other functions cannot be used.\nClose this window to cancel.", "\n他の機能に設定済みのキーは使用できません。\nキャンセルするにはこのウィンドウを閉じてください。" } },
            { "마우스 버튼 감지 등록에 실패했습니다.\n창을 닫고 다시 시도하거나 키보드 키를 지정하세요.", new string[] { "Mouse button detection failed.\nClose and retry, or assign a keyboard key.", "マウスボタンの検出に失敗しました。\n閉じて再試行するか、キーボードのキーを設定してください。" } },
            { "\n다른 키를 누르세요. 취소: 창의 X 버튼", new string[] { "\nPress another key. Close the window to cancel.", "\n別のキーを押してください。閉じるとキャンセルします。" } },
            { " 버튼/키를 떼면 설정됩니다.", new string[] { " will be assigned when released.", "を離すと設定されます。" } },
            { "해당 창의 해당 위치에서만 클리커가 작동하도록 하는 기능입니다. 창을 최소화 할 경우 작동되지 않습니다.\n\n클릭이 발생하지 않는 경우 해당 창에서는 사용할 수 없는 기능이므로 비활성화 후 사용해주세요\n\n입력 종류에 따라 작동 여부가 달라질수 있습니다.\nex) 왼쪽 클릭 작동 O, 키보드 입력 작동 X", new string[] { "Keeps the clicker running only at the selected position in the selected window. It will not work if the window is minimized.\n\nIf clicks do not occur, this window does not support the feature. Turn it off before use.\n\nSupport may vary by input type.\nex) Left click works O, keyboard input works X", "指定したウィンドウの指定位置だけでクリッカーを動作させます。ウィンドウを最小化すると動作しません。\n\nクリックが発生しない場合、このウィンドウでは使用できません。無効にして使用してください。\n\n入力の種類によって動作するかどうかが異なります。\nex) 左クリックは動作 O、キーボード入力は動作 X" } },
            { "마우스 4", new string[] { "Mouse 4", "マウス4" } },
            { "마우스 5", new string[] { "Mouse 5", "マウス5" } },
            { "시작·중지와 긴급 정지 단축키는 달라야 합니다.", new string[] { "Start/stop and emergency stop shortcuts must differ.", "開始・停止と緊急停止のショートカットは別のキーにしてください。" } },
            { "매크로 키와 제어 단축키는 중복될 수 없습니다.", new string[] { "The input key cannot duplicate a control shortcut.", "入力キーと制御ショートカットは重複できません。" } },
            { "반복 입력", new string[] { "Repeated input", "繰り返し入力" } },
            { "입력 종류", new string[] { "Input type", "入力の種類" } },
            { "마우스 왼쪽 클릭", new string[] { "Left mouse click", "マウス左クリック" } },
            { "마우스 오른쪽 클릭", new string[] { "Right mouse click", "マウス右クリック" } },
            { "마우스 휠 클릭 (가운데 버튼)", new string[] { "Middle mouse click (wheel button)", "マウス中央クリック（ホイールボタン）" } },
            { "키보드 키", new string[] { "Keyboard key", "キーボードのキー" } },
            { "설정", new string[] { "Set", "設定" } },
            { "입력 간격", new string[] { "Input interval", "入力間隔" } },
            { "ms  /  최소 10ms", new string[] { "ms  /  minimum 10ms", "ms  /  最小10ms" } },
            { "실행 제어", new string[] { "Run controls", "実行の制御" } },
            { "시작 · 중지", new string[] { "Start / stop", "開始・停止" } },
            { "긴급 정지", new string[] { "Emergency stop", "緊急停止" } },
            { "정지됨", new string[] { "Stopped", "停止しました" } },
            { "대상 창에서 시작 단축키를 누르세요.", new string[] { "Press the start shortcut in the target window.", "対象ウィンドウで開始ショートカットを押してください。" } },
            { "완료한 입력  0회", new string[] { "Inputs completed  0", "完了した入力  0回" } },
            { "설정 자동 저장", new string[] { "Settings saved automatically", "設定を自動保存" } },
            { "실험적 기능", new string[] { "Experimental features", "実験的機能" } },
            { "해당 창에서 유지", new string[] { "Keep in this window", "このウィンドウで維持" } },
            { "해당 창에서 유지 도움말", new string[] { "Keep in this window help", "このウィンドウで維持のヘルプ" } },
            { "호환 테스트", new string[] { "Compatibility test", "互換性テスト" } },
            { "완료한 입력  ", new string[] { "Inputs completed  ", "完了した入力  " } },
            { "회", new string[] { " times", "回" } },
            { "실행 중 · ", new string[] { "Running · ", "実行中 · " } },
            { " 또는 ", new string[] { " or ", " または " } },
            { "로 정지", new string[] { " to stop", "で停止" } },
            { "단축키 등록 실패: 다른 키로 설정하세요.", new string[] { "Shortcut registration failed. Assign another key.", "ショートカットの登録に失敗しました。別のキーを設定してください。" } },
            { "대상 위치에 마우스를 놓고 시작 단축키를 누르세요.\n시작할 때의 창과 위치를 고정합니다.", new string[] { "Place the pointer at the target position and press the start shortcut.\nThe starting window and position will be locked.", "対象位置にマウスを置いて開始ショートカットを押してください。\n開始時のウィンドウと位置を固定します。" } },
            { "꺼짐 · 기존 클리커 방식으로 입력합니다.", new string[] { "Off · Uses the normal clicker input method.", "無効 · 通常のクリッカー方式で入力します。" } },
            { "저장된 설정 불러옴", new string[] { "Saved settings loaded", "保存済み設定を読み込みました" } },
            { "설정 읽기 실패 · 기본값 사용", new string[] { "Could not read settings · Using defaults", "設定の読み込みに失敗 · 初期設定を使用" } },
            { "설정 저장됨", new string[] { "Settings saved", "設定を保存しました" } },
            { "저장 실패 · 폴더 권한 확인", new string[] { "Save failed · Check folder permissions", "保存に失敗 · フォルダーの権限を確認" } },
            { "키를 다시 눌러주세요.", new string[] { "Please press the key again.", "キーをもう一度押してください。" } },
            { "단축키는 Ctrl / Alt / Shift / Windows 키 외의 키 하나를 눌러주세요.", new string[] { "Choose one key other than Ctrl / Alt / Shift / Windows.", "Ctrl / Alt / Shift / Windows以外のキーを1つ選んでください。" } },
            { "매크로 키 지정", new string[] { "Assign input key", "入力キーの設定" } },
            { "시작·중지 키 지정", new string[] { "Assign start/stop shortcut", "開始・停止キーの設定" } },
            { "긴급 정지 키 지정", new string[] { "Assign emergency stop shortcut", "緊急停止キーの設定" } },
            { "매크로 키 설정됨: ", new string[] { "Input key assigned: ", "入力キーを設定: " } },
            { "설정됨 · 시작·중지: ", new string[] { "Assigned · Start/stop: ", "設定済み · 開始・停止: " } },
            { " / 긴급 정지: ", new string[] { " / Emergency stop: ", " / 緊急停止: " } },
            { "등록 실패: 다른 앱이 사용하거나 지원하지 않는 키입니다. 기존 설정을 유지합니다.", new string[] { "Registration failed: the key is in use or unsupported. Previous settings kept.", "登録に失敗しました。使用中または未対応のキーです。以前の設定を維持します。" } },
            { "단축키 등록 실패: 다른 키로 설정해야 시작할 수 있습니다.", new string[] { "Shortcut registration failed. Assign another key before starting.", "ショートカットの登録に失敗しました。開始前に別のキーを設定してください。" } },
            { "●  실행 중", new string[] { "●  Running", "●  実行中" } },
            { "●  대기 중", new string[] { "●  Idle", "●  待機中" } },
            { "시작  ", new string[] { "Start  ", "開始  " } },
            { "정지  ", new string[] { "Stop  ", "停止  " } },
            { " 대상 위치에서 시작 단축키를 누르세요.", new string[] { " Press the start shortcut at the target position.", " 対象位置で開始ショートカットを押してください。" } },
            { "고정된 창: ", new string[] { "Locked window: ", "固定ウィンドウ: " } },
            { "\n위치: ", new string[] { "\nPosition: ", "\n位置: " } },
            { "고정 입력 중지: ", new string[] { "Locked input stopped: ", "固定入力を停止: " } },
            { "입력 실패: 대상 앱의 권한 또는 입력 허용 여부를 확인하세요.", new string[] { "Input failed: check the target app's permissions and input support.", "入力に失敗しました。対象アプリの権限と入力対応を確認してください。" } },
            { "완료한 입력: ", new string[] { "Inputs completed: ", "完了した入力: " } },
            { "정지됨 (", new string[] { "Stopped (", "停止しました（" } },
            { "종료됨", new string[] { "Closed", "終了しました" } },
            { "검사", new string[] { "Test", "テスト" } },
            { "중복 키", new string[] { "Duplicate key", "重複キー" } },
            { "Auto Macro가 이미 실행 중입니다.", new string[] { "Auto Macro is already running.", "Auto Macroは既に実行中です。" } },
            { "마우스 위치를 읽을 수 없습니다.", new string[] { "Could not read the pointer position.", "マウス位置を読み取れません。" } },
            { "Auto Macro 이외의 대상 창 위에 마우스를 놓으세요.", new string[] { "Place the pointer over a window other than Auto Macro.", "Auto Macro以外の対象ウィンドウにマウスを置いてください。" } },
            { "창 내부 위치를 읽을 수 없습니다.", new string[] { "Could not read the position inside the window.", "ウィンドウ内の位置を読み取れません。" } },
            { "대상 창이 변경되거나 종료되었습니다. 다시 지정하세요.", new string[] { "The target window changed or closed. Select it again.", "対象ウィンドウが変更または終了しました。再度指定してください。" } },
            { "지정 위치가 창 내부를 벗어났습니다. 다시 지정하세요.", new string[] { "The position is outside the window. Select it again.", "指定位置がウィンドウの外にあります。再度指定してください。" } },
            { "클릭 누름 전송 실패: 권한 또는 대상 창을 확인하세요.", new string[] { "Mouse-down delivery failed. Check permissions and the target window.", "クリック押下の送信に失敗しました。権限と対象ウィンドウを確認してください。" } },
            { "클릭 해제 전송 실패: 대상 창 상태를 확인하세요.", new string[] { "Mouse-up delivery failed. Check the target window.", "クリック解放の送信に失敗しました。対象ウィンドウを確認してください。" } },
            { "키 누름 전송 실패: 권한 또는 대상 창을 확인하세요.", new string[] { "Key-down delivery failed. Check permissions and the target window.", "キー押下の送信に失敗しました。権限と対象ウィンドウを確認してください。" } },
            { "키 해제 전송 실패: 대상 창을 확인하세요.", new string[] { "Key-up delivery failed. Check the target window.", "キー解放の送信に失敗しました。対象ウィンドウを確認してください。" } },
            { "대상 창을 최소화한 뒤 다시 테스트하세요.", new string[] { "Minimize the target window and try again.", "対象ウィンドウを最小化して再試行してください。" } },
            { "대상 창의 최소화를 해제한 뒤 다시 테스트하세요.", new string[] { "Restore the target window and try again.", "対象ウィンドウの最小化を解除して再試行してください。" } },
            { "다른 창을 활성화한 뒤 다시 테스트하세요.", new string[] { "Activate another window and try again.", "別のウィンドウをアクティブにして再試行してください。" } },
            { "미확인", new string[] { "Unconfirmed", "未確認" } },
            { "클릭 호환성 테스트", new string[] { "Click compatibility test", "クリック互換性テスト" } },
            { "1. 대상 지정 후 3초 안에 클릭할 위치에 마우스를 놓으세요.\n2. 테스트를 누르면 3초 뒤 그 위치에 클릭을 한 번 보냅니다.\n누르면 문제가 없는 위치를 선택하고 실제 반응을 확인하세요.", new string[] { "1. Select the target, then place the pointer within 3 seconds.\n2. A test sends one click after 3 seconds.\nChoose a safe position and check the actual response.", "1. 対象指定後、3秒以内にクリック位置にマウスを置きます。\n2. テストを押すと3秒後に1回クリックを送信します。\n押しても問題ない位置を選び、実際の反応を確認してください。" } },
            { "대상 · 위치 지정 (3초)", new string[] { "Select window / position (3s)", "対象・位置を指定（3秒）" } },
            { "지정된 창 없음", new string[] { "No window selected", "対象ウィンドウなし" } },
            { "왼쪽 클릭", new string[] { "Left click", "左クリック" } },
            { "오른쪽 클릭", new string[] { "Right click", "右クリック" } },
            { "휠 클릭", new string[] { "Middle click", "中央クリック" } },
            { "백그라운드 상태", new string[] { "Background state", "バックグラウンド状態" } },
            { "최소화 상태", new string[] { "Minimized state", "最小化状態" } },
            { "테스트 클릭 (3초 뒤 1회)", new string[] { "Test click (once after 3s)", "テストクリック（3秒後に1回）" } },
            { "예약 취소", new string[] { "Cancel pending test", "予約をキャンセル" } },
            { "대상 창을 지정하세요.", new string[] { "Select a target window.", "対象ウィンドウを指定してください。" } },
            { "반응 있었음", new string[] { "It responded", "反応あり" } },
            { "반응 없었음", new string[] { "No response", "反応なし" } },
            { "예약이 취소되었습니다.", new string[] { "The pending action was canceled.", "予約をキャンセルしました。" } },
            { "이번 대상 · 선택한 클릭 종류의 사용자 확인 결과\n백그라운드: ", new string[] { "User-confirmed results for this target and input type\nBackground: ", "今回の対象・クリック種類の確認結果\nバックグラウンド: " } },
            { "  /  최소화: ", new string[] { "  /  Minimized: ", "  /  最小化: " } },
            { "작동 확인", new string[] { "Works", "動作確認" } },
            { "반응 없음", new string[] { "Did not respond", "反応なし" } },
            { "결과를 기록했습니다. 다른 상태도 따로 테스트할 수 있습니다.", new string[] { "Result recorded. You can test the other state separately.", "結果を記録しました。他の状態も個別にテストできます。" } },
            { "초 뒤 ", new string[] { "s until ", "秒後に " } },
            { "대상 지정 · 원하는 위치에 마우스를 놓으세요.", new string[] { "target capture · Place the pointer at the desired position.", "対象を指定 · マウスを目的の位置に置いてください。" } },
            { "1회 클릭 · 다른 창을 활성화하거나 대상을 최소화하세요.", new string[] { "one click · Activate another window or minimize the target.", "1回クリック · 別のウィンドウを開くか対象を最小化してください。" } },
            { "\n클릭 위치 (대상 컨트롤 기준): ", new string[] { "\nPosition (inside target control): ", "\nクリック位置（対象コントロール内）: " } },
            { "대상 지정 완료. 창 크기와 내부 배치를 유지하고 테스트하세요.", new string[] { "Target selected. Keep its size and layout unchanged.", "対象指定完了。ウィンドウのサイズと内部配置を維持してください。" } },
            { "클릭 메시지 전송 완료. 실제 반응을 확인하고 아래에서 선택하세요.", new string[] { "Click messages sent. Check the actual response and choose below.", "クリックメッセージを送信しました。実際の反応を確認して下で選択してください。" } },
            { "(제목 없는 창)", new string[] { "(Untitled window)", "（タイトルなし）" } },
            { " 누름", new string[] { " down", " 押下" } },
            { " 뗌", new string[] { " up", " 解放" } },
            { "마우스 이동", new string[] { "Mouse movement", "マウス移動" } },
            { "세로 휠 ", new string[] { "Vertical wheel ", "縦ホイール " } },
            { "가로 휠 ", new string[] { "Horizontal wheel ", "横ホイール " } },
            { "왼쪽", new string[] { "Left", "左" } },
            { "오른쪽", new string[] { "Right", "右" } },
            { "가운데", new string[] { "Middle", "中央" } },
            { "버튼 ", new string[] { "Button ", "ボタン " } },
            { "마우스 ", new string[] { "Mouse ", "マウス " } },
            { "키보드 키 또는 마우스 4·5번 버튼 하나를 지정하세요.", new string[] { "Choose one keyboard key or mouse button 4 or 5.", "キーまたはマウスの4・5ボタンを1つ設定してください。" } },
            { "녹화·실행·긴급 정지 단축키는 서로 달라야 합니다.", new string[] { "Record, play and emergency stop shortcuts must differ.", "録画・実行・緊急停止ショートカットは別々にしてください。" } },
            { "이름은 공백을 제외하고 1~80자로 입력하세요.", new string[] { "Enter a name of 1–80 characters, excluding outer spaces.", "前後の空白を除いて1～80文字の名前を入力してください。" } },
            { "이름에 제어 문자를 넣을 수 없습니다.", new string[] { "Names cannot contain control characters.", "名前に制御文字を使用できません。" } },
            { "같은 이름이 있습니다. 다른 이름을 입력하세요.", new string[] { "That name is already used. Enter another name.", "同じ名前が存在します。別の名前を入力してください。" } },
            { "입력을 보내지 못했습니다. 대상 앱의 권한을 확인하세요.", new string[] { "Could not send input. Check the target app's permissions.", "入力を送信できません。対象アプリの権限を確認してください。" } },
            { "마우스 이동: 시간 ", new string[] { "Mouse movement: duration ", "マウス移動: 時間 " } },
            { "개 로그", new string[] { " log entries", "件のログ" } },
            { " · 녹화 내용", new string[] { " · Recorded actions", " · 録画内容" } },
            { "\n  연속된 마우스 이동은 묶어서 표시합니다. + / − 버튼 클릭 또는 묶음 줄 더블클릭으로 펼치거나 접으세요.", new string[] { "\n  Mouse movements are grouped. Click + / − or double-click the summary row to expand or collapse.", "\n  連続したマウス移動をまとめて表示します。+ / −をクリック、またはまとめ行をダブルクリックして展開・折りたたみできます。" } },
            { "순서", new string[] { "Order", "順序" } },
            { "시간 (ms)", new string[] { "Time (ms)", "時間（ms）" } },
            { "입력", new string[] { "Input", "入力" } },
            { "화면 위치", new string[] { "Screen position", "画面位置" } },
            { "●  녹화 중", new string[] { "●  Recording", "●  録画中" } },
            { "▶  재생 중", new string[] { "▶  Playing", "▶  再生中" } },
            { "녹화", new string[] { "Recording", "録画" } },
            { "녹화 시작", new string[] { "Start recording", "録画開始" } },
            { "녹화 저장", new string[] { "Save recording", "録画を保存" } },
            { "저장된 매크로", new string[] { "Saved macros", "保存済みマクロ" } },
            { "우클릭으로 옵션 열기", new string[] { "Right-click for options", "右クリックでオプション" } },
            { "이름", new string[] { "Name", "名前" } },
            { "길이", new string[] { "Duration", "長さ" } },
            { "동작 수", new string[] { "Actions", "動作数" } },
            { "내용 보기", new string[] { "View contents", "内容を表示" } },
            { "이름 변경", new string[] { "Rename", "名前を変更" } },
            { "반복 설정", new string[] { "Repeat settings", "繰り返し設定" } },
            { "삭제", new string[] { "Delete", "削除" } },
            { "선택한 매크로 실행", new string[] { "Run selected macro", "選択したマクロを実行" } },
            { "녹화 시작 · 종료", new string[] { "Record / finish", "録画開始・終了" } },
            { "선택 매크로 실행", new string[] { "Run selected macro", "選択マクロを実行" } },
            { "대상 앱에서 녹화 단축키를 누르세요. 마우스 위치와 키보드 입력을 기록합니다.", new string[] { "Press the recording shortcut in the target app to record mouse and keyboard input.", "対象アプリで録画ショートカットを押すとマウスとキー入力を記録します。" } },
            { "매크로 파일을 읽지 못했습니다. 원본을 보존했습니다. MacroLibrary.json과 .bak 파일을 확인하세요.", new string[] { "Could not read macros. Original files kept. Check MacroLibrary.json and its .bak file.", "マクロを読み込めません。元ファイルは保持しました。MacroLibrary.jsonと.bakを確認してください。" } },
            { "매크로 ", new string[] { "Macro ", "マクロ " } },
            { "■ 녹화 종료", new string[] { "■ Finish recording", "■ 録画終了" } },
            { "● 녹화 시작", new string[] { "● Start recording", "● 録画開始" } },
            { "녹화 중  ", new string[] { "Recording  ", "録画中  " } },
            { "개 동작", new string[] { " actions", "件の動作" } },
            { "재생 중  ", new string[] { "Playing  ", "再生中  " } },
            { "회차  ·  ", new string[] { " iteration · ", "回目 · " } },
            { "개 동작 완료", new string[] { " actions completed", "件の動作完了" } },
            { "새 녹화를 만들고 이름을 입력해 저장하세요.", new string[] { "Record a macro, name it, and save.", "新しいマクロを録画し、名前を入力して保存してください。" } },
            { "저장 전 녹화  ", new string[] { "Unsaved recording  ", "未保存の録画  " } },
            { "입력 감지를 시작하지 못했습니다. Clicker로 이동한 후 Macro를 다시 열어주세요.", new string[] { "Input detection failed. Switch to Clicker, then return to Macro.", "入力検出に失敗しました。Clickerに移動し、Macroを開き直してください。" } },
            { "녹화 한도에 도달해 종료했습니다. 녹화 내용을 저장하세요.", new string[] { "Recording limit reached. Save your recording.", "録画上限に達しました。録画内容を保存してください。" } },
            { "아직 저장하지 않은 녹화가 있습니다. 새 녹화로 바꿀까요?", new string[] { "An unsaved recording exists. Replace it with a new recording?", "未保存の録画があります。新しい録画に置き換えますか？" } },
            { "녹화 중입니다. ", new string[] { "Recording. Press ", "録画中です。" } },
            { "로 종료한 뒤 이름을 입력하고 저장하세요.", new string[] { " to finish, then enter a name and save.", "で終了し、名前を入力して保存してください。" } },
            { "기록된 동작이 없습니다. 대상 앱에서 입력한 뒤 다시 종료하세요.", new string[] { "No actions recorded. Try input in the target app and finish again.", "動作が記録されていません。対象アプリで入力してから終了してください。" } },
            { "녹화가 끝났습니다. 이름을 입력하고 ‘녹화 저장’을 누르세요.", new string[] { "Recording finished. Enter a name and select Save recording.", "録画が終了しました。名前を入力し「録画を保存」を押してください。" } },
            { "저장하지 못했습니다. 폴더 권한과 여유 공간을 확인하세요. 변경 사항은 적용하지 않았습니다.", new string[] { "Save failed. Check permissions and free space. Changes were not applied.", "保存に失敗しました。権限と空き容量を確認してください。変更は適用していません。" } },
            { "저장했습니다. 목록에서 선택하고 실행할 수 있습니다.", new string[] { "Saved. Select it from the list to run.", "保存しました。一覧から選択して実行できます。" } },
            { "저장", new string[] { "Save", "保存" } },
            { "저장하지 못했습니다. 다시 시도하세요.", new string[] { "Could not save. Try again.", "保存できません。再試行してください。" } },
            { "이름을 변경했습니다.", new string[] { "Name changed.", "名前を変更しました。" } },
            { "반복 설정을 저장했습니다.", new string[] { "Repeat settings saved.", "繰り返し設定を保存しました。" } },
            { "반복 횟수 설정", new string[] { "Repeat count", "繰り返し回数" } },
            { "계속 반복", new string[] { "Repeat forever", "無限に繰り返す" } },
            { "반복 사이 대기 시간 (ms)", new string[] { "Wait between repeats (ms)", "繰り返し間の待機時間（ms）" } },
            { "전체 재생 횟수입니다. 마지막 재생 뒤에는 대기하지 않습니다.", new string[] { "Total playback count. No wait after the last playback.", "合計再生回数です。最後の再生後は待機しません。" } },
            { "취소", new string[] { "Cancel", "キャンセル" } },
            { "저장하지 못했습니다. 설정을 확인하고 다시 시도하세요.", new string[] { "Could not save. Check the settings and try again.", "保存できません。設定を確認して再試行してください。" } },
            { "매크로를 삭제했습니다.", new string[] { "Macro deleted.", "マクロを削除しました。" } },
            { "매크로 삭제", new string[] { "Delete macro", "マクロの削除" } },
            { "다음 매크로를 정말 삭제할까요?", new string[] { "Delete the following macro?", "次のマクロを削除しますか？" } },
            { "삭제 내용을 저장하지 못했습니다. 다시 시도하세요.", new string[] { "Could not save the deletion. Try again.", "削除を保存できません。再試行してください。" } },
            { "계속 반복합니다.", new string[] { "Repeats forever.", "無限に繰り返します。" } },
            { "회 재생합니다.", new string[] { " total plays.", "回再生します。" } },
            { " 반복 사이 ", new string[] { " Wait between repeats: ", " 繰り返し間: " } },
            { "ms 대기 · ", new string[] { "ms · ", "ms待機 · " } },
            { "재생을 완료했습니다.", new string[] { "Playback completed.", "再生が完了しました。" } },
            { "재생을 정지했습니다.", new string[] { "Playback stopped.", "再生を停止しました。" } },
            { "재생 중단: ", new string[] { "Playback interrupted: ", "再生中断: " } },
            { "Macro 단축키 설정", new string[] { "Macro shortcuts", "Macroのショートカット設定" } },
            { "단축키를 설정하고 저장했습니다.", new string[] { "Shortcut assigned and saved.", "ショートカットを設定・保存しました。" } },
            { "저장하지 않은 녹화가 있습니다. 현재 이름으로 저장하고 종료할까요?", new string[] { "An unsaved recording exists. Save with its current name and exit?", "未保存の録画があります。現在の名前で保存して終了しますか？" } },
            { "녹화 내용 편집", new string[] { "Edit recording", "録画内容の編集" } },
            { "복제", new string[] { "Duplicate", "複製" } },
            { "내보내기", new string[] { "Export", "エクスポート" } },
            { "가져오기", new string[] { "Import", "インポート" } },
            { "Ctrl/Shift로 여러 줄을 선택할 수 있습니다. 이동 묶음 줄은 전체 로그를 선택합니다. + / − 버튼으로만 펼치거나 접습니다.", new string[] { "Use Ctrl/Shift to select multiple rows. A movement summary selects its entire group. Only + / − expands or collapses.", "Ctrl/Shiftで複数行を選択できます。移動まとめ行は全ログを選択します。+ / −ボタンのみで展開・折りたたみます。" } },
            { "이 동작 전 대기 (ms)", new string[] { "Delay before action (ms)", "この動作前の待機 (ms)" } },
            { "시간 적용", new string[] { "Apply delay", "時間を適用" } },
            { "선택 동작 삭제", new string[] { "Delete selected actions", "選択した動作を削除" } },
            { "전체 길이는 24시간을 넘을 수 없습니다.", new string[] { "Total duration cannot exceed 24 hours.", "全体の長さは24時間を超えられません。" } },
            { "시간을 수정했습니다. 저장을 눌러 반영하세요.", new string[] { "Delay changed. Save to keep your edits.", "時間を変更しました。保存して反映してください。" } },
            { "동작을 삭제했습니다. 저장을 눌러 반영하세요.", new string[] { "Actions deleted. Save to keep your edits.", "動作を削除しました。保存して反映してください。" } },
            { "녹화 내용을 저장했습니다.", new string[] { "Recording changes saved.", "録画内容を保存しました。" } },
            { " 복사본", new string[] { " copy", " コピー" } },
            { "매크로를 복제했습니다.", new string[] { "Macro duplicated.", "マクロを複製しました。" } },
            { "파일에 저장된 매크로가 없습니다.", new string[] { "No macros were found in this file.", "ファイルにマクロがありません。" } },
            { "{0}개 매크로를 가져왔습니다.", new string[] { "Imported {0} macros.", "{0}個のマクロをインポートしました。" } },
            { "매크로 JSON 파일을 읽지 못했습니다. 기존 목록은 유지됩니다.", new string[] { "Could not read the macro JSON file. Your existing list is unchanged.", "マクロJSONを読み込めません。既存の一覧は維持されます。" } },
            { "매크로 가져오기", new string[] { "Import macros", "マクロのインポート" } },
            { "매크로 내보내기", new string[] { "Export macro", "マクロのエクスポート" } },
            { "매크로를 내보냈습니다.", new string[] { "Macro exported.", "マクロをエクスポートしました。" } },
            { "내보내지 못했습니다. 폴더 권한과 여유 공간을 확인하세요.", new string[] { "Export failed. Check folder permissions and available space.", "エクスポートできません。フォルダー権限と空き容量を確認してください。" } },
            { "{0}/{1}회 · {2}/{3} 동작", new string[] { "Repeat {0}/{1} · Actions {2}/{3}", "{0}/{1}回 · 動作 {2}/{3}" } },
            { "다음 반복까지 {0}초", new string[] { "Next repeat in {0}s", "次の繰り返しまで{0}秒" } },
            { "현재 버전: {0}", new string[] { "Current version: {0}", "現在のバージョン: {0}" } },
            { "사용자 데이터 파일에는 내보낼 수 없습니다.", new string[] { "Choose a file other than your user data files.", "ユーザーデータファイルにはエクスポートできません。" } },
            { "설정 복구", new string[] { "Recover settings", "設定の復旧" } },
            { "설정 파일이 손상되었습니다. 원본을 백업하고 기본 설정으로 복구할 수 있습니다. 저장된 매크로는 유지됩니다.", new string[] { "Settings are damaged. Back up the original and restore defaults. Saved macros will be kept.", "設定ファイルが破損しています。元ファイルをバックアップして初期設定に復旧できます。保存済みマクロは維持されます。" } },
            { "기본 설정으로 복구", new string[] { "Restore default settings", "初期設定に復旧" } },
            { "복구하지 못했습니다. 폴더 권한과 여유 공간을 확인하세요.", new string[] { "Recovery failed. Check folder permissions and free space.", "復旧できません。フォルダー権限と空き容量を確認してください。" } },
            { "설정 초기화", new string[] { "Reset settings", "設定を初期化" } },
            { "매크로 백업 복원", new string[] { "Restore macro backup", "マクロのバックアップを復元" } },
            { "클리커 설정·언어·안내 확인을 초기화할까요? 현재 설정은 백업하고 저장된 매크로는 유지합니다.", new string[] { "Reset clicker settings, language, and notice acknowledgement? Current settings will be backed up and saved macros kept.", "クリッカー設定・言語・案内確認を初期化しますか？現在の設定をバックアップし、保存済みマクロは維持します。" } },
            { "설정을 초기화했습니다. 안내는 다음 실행에 다시 표시됩니다.", new string[] { "Settings reset. The notice will appear at next startup.", "設定を初期化しました。次回起動時に案内が表示されます。" } },
            { "매크로 백업을 복원했습니다.", new string[] { "Macro backup restored.", "マクロのバックアップを復元しました。" } },
            { "백업을 복원하지 못했습니다. 기존 파일과 목록은 유지됩니다.", new string[] { "Backup restoration failed. Existing files and list are unchanged.", "バックアップを復元できません。既存のファイルと一覧は維持されます。" } },
            { "현재 매크로 목록과 단축키를 백업 내용으로 바꿀까요? 현재 파일은 별도로 보존합니다.", new string[] { "Replace the macro list and shortcuts with this backup? The current file will be preserved separately.", "マクロ一覧とショートカットをバックアップの内容に置き換えますか？現在のファイルは別に保持します。" } },
            { "실행 취소", new string[] { "Undo", "元に戻す" } },
            { "이전 편집으로 되돌렸습니다.", new string[] { "Previous edit restored.", "前の編集に戻しました。" } },
            { "언어 설정을 저장하지 못했습니다. 설정 초기화로 복구하거나 폴더 권한을 확인하세요.", new string[] { "Could not save language. Reset settings to recover, or check folder permissions.", "言語を保存できません。設定を初期化して復旧するか、フォルダー権限を確認してください。" } },
        };
    }
}
