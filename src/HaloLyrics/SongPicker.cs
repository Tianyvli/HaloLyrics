namespace HaloLyrics;

public sealed class SongPicker : Form
{
    public CatalogSong? Selected { get; private set; }
    public SongPicker(Playback playback, List<CatalogSong> songs)
    {
        Text = "确认 Apple Music 曲目版本";
        Size = new(1000, 440);
        StartPosition = FormStartPosition.CenterParent;
        var label = new Label { Text = $"正在播放：{playback.Title} — {playback.Artist} / {playback.Album}\n请选择同一个录音版本；确认后记住对应关系。", Dock = DockStyle.Top, Height = 65, Padding = new(12) };
        var list = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true, IntegralHeight = false };
        foreach (var song in songs) list.Items.Add(song);
        if (songs.Count > 0) list.SelectedIndex = 0;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft };
        var confirm = new Button { Text = "使用所选版本", AutoSize = true };
        var cancel = new Button { Text = "暂不显示", AutoSize = true };
        confirm.Click += (_, _) => { Selected = list.SelectedItem as CatalogSong; if (Selected is not null) { DialogResult = DialogResult.OK; Close(); } };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.Add(confirm); buttons.Controls.Add(cancel);
        Controls.Add(list); Controls.Add(label); Controls.Add(buttons);
    }
}
