using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ExIconChanger;

public sealed class MainForm : Form
{
    private string? _icoPath;

    private readonly Panel _dropPanel;
    private readonly PictureBox _preview;
    private readonly Label _lblDrop;
    private readonly Label _lblIcoPath;
    private readonly Button _btnBrowse;
    private readonly TextBox _txtExt;
    private readonly Button _btnApply;
    private readonly Button _btnDefault;
    private readonly Label _lblStatus;

    public MainForm()
    {
        Text = "확장자 아이콘 변경기";
        ClientSize = new Size(460, 490);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;

        // ---- 1. 드롭 영역 ----
        var grpIco = new GroupBox
        {
            Text = "1. ICO 파일 (드래그 또는 선택)",
            Location = new Point(12, 12),
            Size = new Size(436, 200)
        };

        _dropPanel = new Panel
        {
            Location = new Point(10, 22),
            Size = new Size(416, 168),
            BorderStyle = BorderStyle.FixedSingle,
            AllowDrop = true,
            BackColor = Color.WhiteSmoke
        };

        _preview = new PictureBox
        {
            Location = new Point(12, 12),
            Size = new Size(96, 96),
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle,
            AllowDrop = true
        };

        _lblDrop = new Label
        {
            Location = new Point(120, 12),
            Size = new Size(284, 60),
            Text = "여기에 .ico 파일을 드래그하세요.\r\n\r\n예: myicon.ico",
            AllowDrop = true
        };

        _lblIcoPath = new Label
        {
            Location = new Point(120, 76),
            Size = new Size(284, 32),
            ForeColor = Color.Gray,
            Text = "선택된 파일 없음",
            AllowDrop = true
        };

        _btnBrowse = new Button
        {
            Location = new Point(120, 114),
            Size = new Size(140, 30),
            Text = "파일 선택..."
        };
        _btnBrowse.Click += OnBrowse;

        _dropPanel.Controls.Add(_preview);
        _dropPanel.Controls.Add(_lblDrop);
        _dropPanel.Controls.Add(_lblIcoPath);
        _dropPanel.Controls.Add(_btnBrowse);
        grpIco.Controls.Add(_dropPanel);
        Controls.Add(grpIco);

        // ---- 2. 확장자 입력 ----
        var grpExt = new GroupBox
        {
            Text = "2. 확장자 입력",
            Location = new Point(12, 220),
            Size = new Size(436, 70)
        };

        var lblDot = new Label { Location = new Point(14, 32), Size = new Size(20, 23), Text = "." };
        _txtExt = new TextBox
        {
            Location = new Point(34, 29),
            Size = new Size(180, 23),
            PlaceholderText = "예: rar"
        };
        _txtExt.TextChanged += (_, _) => UpdateStatusIdle();

        var lblHint = new Label
        {
            Location = new Point(224, 32),
            Size = new Size(200, 23),
            ForeColor = Color.Gray,
            Text = "점(.) 없이 입력"
        };

        grpExt.Controls.Add(lblDot);
        grpExt.Controls.Add(_txtExt);
        grpExt.Controls.Add(lblHint);
        Controls.Add(grpExt);

        // ---- 3. 버튼 ----
        _btnApply = new Button
        {
            Location = new Point(12, 300),
            Size = new Size(214, 45),
            Text = "적용",
            BackColor = Color.LightGreen
        };
        _btnApply.Click += OnApply;

        _btnDefault = new Button
        {
            Location = new Point(234, 300),
            Size = new Size(214, 45),
            Text = "기본값으로 복원"
        };
        _btnDefault.Click += OnReset;

        Controls.Add(_btnApply);
        Controls.Add(_btnDefault);

        // ---- 상태 + 탐색기 재시작 ----
        var btnRestartExplorer = new Button
        {
            Location = new Point(12, 350),
            Size = new Size(436, 28),
            Text = "탐색기 재시작 (아이콘이 바로 안 바뀌면 클릭)"
        };
        btnRestartExplorer.Click += (_, _) =>
        {
            IconManager.RestartExplorer();
            SetStatus("탐색기를 재시작했습니다. 잠시 후 아이콘을 확인하세요.");
        };
        Controls.Add(btnRestartExplorer);

        var btnClearCache = new Button
        {
            Location = new Point(12, 383),
            Size = new Size(436, 28),
            Text = "아이콘 캐시 삭제 + 탐색기 재시작 (안 바뀔 때 클릭)"
        };
        btnClearCache.Click += (_, _) =>
        {
            btnClearCache.Enabled = false;
            try
            {
                string msg = IconManager.ClearIconCacheAndRestartExplorer();
                SetStatus(msg + "\r\n30초 정도 기다린 뒤 아이콘을 확인하세요.");
            }
            catch (Exception ex)
            {
                SetStatus($"캐시 삭제 실패: {ex.Message}", isError: true);
            }
            finally
            {
                btnClearCache.Enabled = true;
            }
        };
        Controls.Add(btnClearCache);

        _lblStatus = new Label
        {
            Location = new Point(12, 416),
            Size = new Size(436, 64),
            ForeColor = Color.DimGray,
            Text = "대기 중. ICO 선택 → 확장자 입력 → 적용."
        };
        Controls.Add(_lblStatus);

        // 드래그 이벤트 (폼 전체 + 패널 + 자식 컨트롤)
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        _dropPanel.DragEnter += OnDragEnter;
        _dropPanel.DragDrop += OnDragDrop;
        _preview.DragEnter += OnDragEnter;
        _preview.DragDrop += OnDragDrop;
        _lblDrop.DragEnter += OnDragEnter;
        _lblDrop.DragDrop += OnDragDrop;
        _lblIcoPath.DragEnter += OnDragEnter;
        _lblIcoPath.DragDrop += OnDragDrop;
    }

    // ---------- 드래그 ----------
    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files is { Length: 1 } && files[0].EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            {
                e.Effect = DragDropEffects.Copy;
                return;
            }
        }
        e.Effect = DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        string[]? files = e.Data?.GetData(DataFormats.FileDrop) as string[];
        if (files is { Length: >= 1 })
            SetIcoFile(files[0]);
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "아이콘 파일 (*.ico)|*.ico",
            Title = "ICO 파일 선택"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            SetIcoFile(dlg.FileName);
    }

    private void SetIcoFile(string path)
    {
        if (!File.Exists(path))
        {
            SetStatus("파일을 찾을 수 없습니다.", isError: true);
            return;
        }
        if (!path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("ICO 파일(.ico)만 사용할 수 있습니다.", isError: true);
            return;
        }

        _icoPath = path;
        _lblIcoPath.Text = path;
        _lblIcoPath.ForeColor = Color.Black;

        try
        {
            _preview.Image?.Dispose();
            using var icon = new Icon(path);
            _preview.Image = icon.ToBitmap();
            SetStatus($"ICO 인식됨: {Path.GetFileName(path)} → 확장자를 입력하고 [적용]을 누르세요.");
        }
        catch (Exception ex)
        {
            SetStatus($"ICO 미리보기를 불러오지 못했습니다: {ex.Message}", isError: true);
        }
    }

    // ---------- 적용 ----------
    private void OnApply(object? sender, EventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_icoPath) || !File.Exists(_icoPath))
            {
                SetStatus("먼저 ICO 파일을 드래그하거나 선택하세요.", isError: true);
                return;
            }

            string ext = IconManager.NormalizeExtension(_txtExt.Text);
            string extName = IconManager.ExtensionWithoutDot(ext);

            // 아이콘 파일을 AppData에 복사해 두어야 나중에 원본을 지워도 유지됨
            string destDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ex-icon-changer", "icons");
            Directory.CreateDirectory(destDir);
            string dest = Path.Combine(destDir, extName + ".ico");
            File.Copy(_icoPath, dest, overwrite: true);

            IconManager.SetCustomIcon(ext, dest);

            string? cur = IconManager.GetCurrentOverride(ext);
            SetStatus($"완료: *{ext} 파일 아이콘이 변경되었습니다.\r\n{cur}\r\n바로 안 바뀌면 탐색기를 다시 시작하세요.");
        }
        catch (Exception ex)
        {
            SetStatus($"적용 실패: {ex.Message}", isError: true);
        }
    }

    // ---------- 기본값 복원 ----------
    private void OnReset(object? sender, EventArgs e)
    {
        try
        {
            string ext = IconManager.NormalizeExtension(_txtExt.Text);
            IconManager.ResetToDefault(ext);
            SetStatus($"완료: *{ext} 아이콘이 Windows 기본값으로 돌아갑니다.\r\n바로 안 바뀌면 탐색기를 다시 시작하세요.");
        }
        catch (Exception ex)
        {
            SetStatus($"복원 실패: {ex.Message}", isError: true);
        }
    }

    private void UpdateStatusIdle()
    {
        // 입력 중에는 상태창을 바로 에러로 덮지 않음
    }

    private void SetStatus(string msg, bool isError = false)
    {
        _lblStatus.Text = msg;
        _lblStatus.ForeColor = isError ? Color.Red : Color.DimGray;
    }
}
