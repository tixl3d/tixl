using System.Drawing;
using System.Windows.Forms;
using T3.SystemUi;

namespace T3.Editor.SplashScreen;

internal sealed class SplashScreen : ISplashScreen
{
    private sealed class SplashForm : Form
    {
        public void PreventFlickering()
        {
            SetStyle(ControlStyles.DoubleBuffer
                     | ControlStyles.UserPaint
                     | ControlStyles.AllPaintingInWmPaint, true);
            UpdateStyles();
        }
    }

    public void Show(string imagePath)
    {
        var backgroundImage = Image.FromFile(imagePath);
        var imageSize = GetScaledSize(backgroundImage);

        _splashForm = new SplashForm
                          {
                              FormBorderStyle = FormBorderStyle.None,
                              StartPosition = FormStartPosition.CenterScreen,
                              BackgroundImage = backgroundImage,
                              BackgroundImageLayout = ImageLayout.Stretch,
                              Size = imageSize,
                          };
        _splashForm.PreventFlickering();

        var tableLayoutPanel = new TableLayoutPanel
                                   {
                                       Dock = DockStyle.Fill,
                                       ColumnCount = 2,
                                       RowCount = 1,
                                       CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
                                       BackColor = Color.Transparent,
                                       Padding = new Padding(0,0,0,5),
                                   };

        tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
        tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 80F));

        tableLayoutPanel.Controls.Add(new Label
                                          {
                                              Dock = DockStyle.Fill,
                                              AutoSize = false,
                                              TextAlign = ContentAlignment.BottomLeft,
                                              BackColor = Color.Transparent,
                                              ForeColor = Color.DimGray,
                                              Text = Program.FormattedEditorVersion,
                                              UseMnemonic = false,
                                              Font = new Font("Arial", 8),
                                              Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                                              Size = new Size(400, 16)
                                          }, 0, 0);

        _logMessageLabel = new Label
                               {
                                   Dock = DockStyle.Bottom,
                                   AutoSize = false,
                                   TextAlign = ContentAlignment.BottomRight,
                                   BackColor = Color.Transparent,
                                   ForeColor = Color.DimGray,
                                   Text = @"Loading T3...",
                                   UseMnemonic = false,
                                   Font = new Font("Arial", 8),
                                   Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                                   Size = new Size(400, 16)
                               };

        tableLayoutPanel.Controls.Add(_logMessageLabel, 1, 0);

        _splashForm.Controls.Add(tableLayoutPanel);

        _splashForm.Show();
        _splashForm.Refresh();
    }

    public void Close()
    {
        _splashForm.Close();
        _splashForm = null;
    }

    private static Size GetScaledSize(Image image)
    {
        using var graphics = Graphics.FromHwnd(IntPtr.Zero);

        var dpiX = graphics.DpiX;
        var dpiY = graphics.DpiY;

        var width = (int)(image.Width * (dpiX / _baseDpi.Width));
        var height = (int)(image.Height * (dpiY / _baseDpi.Height));

        return new Size(width, height);
    }

    public void Dispose()
    {
    }

    /// <summary>
    /// Defer the setting the form UI element on the main Thread.
    /// </summary>
    private delegate void SafeCallDelegate(string text);

    private void WriteTextSafe(string text)
    {
        if (_logMessageLabel.InvokeRequired)
        {
            if (!_invoked)
                return;

            var d = new SafeCallDelegate(WriteTextSafe);
            _logMessageLabel.Invoke(d, text);
            _invoked = true;
        }
        else
        {
            _logMessageLabel.Text = text;
            _logMessageLabel.Refresh();
            //_splashForm.Refresh();
            _invoked = false;
        }
    }

    private bool _invoked;

    #region implement ILogWriter
    public ILogEntry.EntryLevel Filter { get; set; }

    public void ProcessEntry(ILogEntry entry)
    {
        if (_logMessageLabel == null)
            return;

        var firstLine = entry.Message.Split("\n").First();
        WriteTextSafe(firstLine[..Math.Min(60, firstLine.Length)]);
    }
    #endregion

    private static readonly Size _baseDpi = new(96, 96);
    private SplashForm _splashForm;
    private Label _logMessageLabel;
}