using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace DownTrack;

public sealed class MainWindow : Window, INotifyPropertyChanged
{
    readonly string stateFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DownTrack", "state.json");
    readonly string toolsFolder = Path.Combine(AppContext.BaseDirectory, "tools");
    readonly ObservableCollection<string> rootFolders = [];
    readonly ObservableCollection<MediaItem> items = [];
    readonly ObservableCollection<PendingChange> pending = [];
    readonly List<(string code,string name)> languages =
    [
        ("auto","Automatic"),("en","English"),("he","עברית"),("es","Español"),("fr","Français"),("de","Deutsch"),("it","Italiano"),("pt","Português"),("nl","Nederlands"),("pl","Polski"),("cs","Čeština"),("tr","Türkçe"),("uk","Українська"),("ru","Русский"),("ar","العربية"),("el","Ελληνικά"),("ro","Română"),("ja","日本語"),("ko","한국어"),("zh-CN","简体中文"),("zh-TW","繁體中文")
    ];
    readonly Dictionary<string, Dictionary<string, string>> translations = new()
    {
        ["en"]=new(){["Library"]="Library",["LibrarySubtitle"]="Your folders, media and pending work in one place.",["AddRoot"]="Add root folder",["AddMedia"]="Add media",["AddFolder"]="Add folder",["Rename"]="Rename",["Delete"]="Delete",["Settings"]="Settings",["Search"]="Search files…",["Pending"]="Pending Changes",["Save"]="Save Changes",["Empty"]="This folder is empty",["Cancel"]="Cancel",["Paste"]="Paste",["AddToLibrary"]="Add to Library",["Preview"]="Media Preview",["SettingsSubtitle"]="Tune DownTrack to your workflow.",["Language"]="Language",["Appearance"]="Appearance",["MediaPreferences"]="Media preferences",["Format"]="Default format",["Quality"]="Audio quality",["Engine"]="Media engine",["CheckEngine"]="Check for Engine Updates",["About"]="About",["CheckUpdates"]="Check for Updates",["System"]="System",["Light"]="Light",["Dark"]="Dark",["DownloadHint"]="Paste a YouTube video or playlist link. Details load quietly.",["PreviewHint"]="Paste a valid link to fetch media details."]},
        ["he"]=new(){["Library"]="ספרייה",["LibrarySubtitle"]="התיקיות, המדיה והשינויים הממתינים שלך במקום אחד.",["AddRoot"]="הוסף תיקייה ראשית",["AddMedia"]="הוסף מדיה",["AddFolder"]="הוסף תיקייה",["Rename"]="שנה שם",["Delete"]="מחק",["Settings"]="הגדרות",["Search"]="חיפוש קבצים…",["Pending"]="שינויים ממתינים",["Save"]="שמור שינויים",["Empty"]="התיקייה ריקה",["Cancel"]="ביטול",["Paste"]="הדבק",["AddToLibrary"]="הוסף לספרייה",["Preview"]="תצוגת מדיה",["SettingsSubtitle"]="התאם את DownTrack לדרך העבודה שלך.",["Language"]="שפה",["Appearance"]="מראה",["MediaPreferences"]="העדפות מדיה",["Format"]="פורמט ברירת מחדל",["Quality"]="איכות שמע",["Engine"]="מנוע מדיה",["CheckEngine"]="בדוק עדכוני מנוע",["About"]="אודות",["CheckUpdates"]="בדוק עדכונים",["System"]="כמו המערכת",["Light"]="בהיר",["Dark"]="כהה",["DownloadHint"]="הדבק קישור מסרטון או פלייליסט ב-YouTube. הפרטים נטענים ברקע.",["PreviewHint"]="הדבק קישור תקין כדי לטעון פרטי מדיה."]},
        ["es"]=new(){["Library"]="Biblioteca",["LibrarySubtitle"]="Tus carpetas, medios y cambios pendientes en un solo lugar.",["AddRoot"]="Añadir carpeta raíz",["AddMedia"]="Añadir multimedia",["AddFolder"]="Añadir carpeta",["Rename"]="Cambiar nombre",["Delete"]="Eliminar",["Settings"]="Configuración",["Search"]="Buscar archivos…",["Pending"]="Cambios pendientes",["Save"]="Guardar cambios",["Empty"]="Esta carpeta está vacía",["Cancel"]="Cancelar",["Paste"]="Pegar",["AddToLibrary"]="Añadir a la biblioteca",["Preview"]="Vista previa",["SettingsSubtitle"]="Adapta DownTrack a tu flujo de trabajo.",["Language"]="Idioma",["Appearance"]="Apariencia",["MediaPreferences"]="Preferencias multimedia",["Format"]="Formato predeterminado",["Quality"]="Calidad de audio",["Engine"]="Motor multimedia",["CheckEngine"]="Buscar actualizaciones del motor",["About"]="Acerca de",["CheckUpdates"]="Buscar actualizaciones",["System"]="Sistema",["Light"]="Claro",["Dark"]="Oscuro",["DownloadHint"]="Pega un enlace de YouTube. Los detalles se cargan en segundo plano.",["PreviewHint"]="Pega un enlace válido para cargar los detalles."]}}
    };
    CancellationTokenSource? inspectCts;
    string language="auto",theme="System",filter="All",search="",defaultFormat="MP3";
    int defaultBitrate=128;
    string? selectedRoot;
    MediaItem? selectedItem;
    string page="Library";

    public MainWindow(){InitializeComponent();DataContext=this;Loaded+=async(_,_)=>{WindowState=WindowState.Maximized;ApplyMica();await LoadStateAsync();};}

    public ObservableCollection<string> RootFolders=>rootFolders;
    public ObservableCollection<MediaItem> Items=>items;
    public string? SelectedRoot{get=>selectedRoot;set{selectedRoot=value;Breadcrumb=value??"Library";OnPropertyChanged();RefreshItems();}}
    public MediaItem? SelectedItem{get=>selectedItem;set{selectedItem=value;OnPropertyChanged();}}
    public string Search{get=>search;set{search=value;OnPropertyChanged();RefreshItems();}}
    public string Filter{get=>filter;set{filter=value;OnPropertyChanged();RefreshItems();}}
    public string Breadcrumb{get;private set;}="Library";
    public string Page{get=>page;private set{page=value;OnPropertyChanged();OnPropertyChanged(nameof(LibraryVisibility));OnPropertyChanged(nameof(SettingsVisibility));}}
    public Visibility LibraryVisibility=>Page=="Library"?Visibility.Visible:Visibility.Collapsed;
    public Visibility SettingsVisibility=>Page=="Settings"?Visibility.Visible:Visibility.Collapsed;
    public bool DownloadVisible{get;private set;}
    public Visibility DownloadVisibility=>DownloadVisible?Visibility.Visible:Visibility.Collapsed;
    public Visibility PendingVisibility=>pending.Count>0?Visibility.Visible:Visibility.Collapsed;
    public string PendingSummary=>$"{pending.Count} changes are waiting to be saved to disk.";
    public string LanguageCode{get=>language;set{language=value;ApplyLanguage();_=SaveStateAsync();}}
    public string Theme{get=>theme;set{theme=value;ApplyTheme(value);OnPropertyChanged();_=SaveStateAsync();}}
    public bool SystemTheme{get=>Theme=="System";set{if(value)Theme="System";}}
    public bool LightTheme{get=>Theme=="Light";set{if(value)Theme="Light";}}
    public bool DarkTheme{get=>Theme=="Dark";set{if(value)Theme="Dark";}}
    public string DefaultFormat{get=>defaultFormat;set{defaultFormat=value;OnPropertyChanged();_=SaveStateAsync();}}
    public int DefaultBitrate{get=>defaultBitrate;set{defaultBitrate=value;OnPropertyChanged();_=SaveStateAsync();}}
    public LanguageOption? SelectedLanguage{get{var x=languages.FirstOrDefault(x=>x.code==language);return new LanguageOption(x.code,x.name);}set{if(value!=null)LanguageCode=value.Code;}}
    public IReadOnlyList<LanguageOption> Languages=>languages.Select(x=>new LanguageOption(x.code,x.name)).ToList();
    public IReadOnlyList<string> Formats=>["MP3","MP4"];
    public IReadOnlyList<int> Bitrates=>[128,192,256,320];

    string EffectiveLanguage(){if(language!="auto")return language;var ui=CultureInfo.CurrentUICulture.Name;return ui.StartsWith("he")?"he":ui.StartsWith("es")?"es":"en";}
    string T(string key)=>translations.TryGetValue(EffectiveLanguage(),out var d)&&d.TryGetValue(key,out var v)?v:translations["en"].GetValueOrDefault(key,key);
    public string LibraryTitle=>T("Library");public string LibrarySubtitle=>T("LibrarySubtitle");public string AddRootText=>T("AddRoot");public string AddMediaText=>T("AddMedia");public string AddFolderText=>T("AddFolder");public string RenameText=>T("Rename");public string DeleteText=>T("Delete");public string SettingsText=>T("Settings");public string SearchText=>T("Search");public string PendingText=>T("Pending");public string SaveText=>T("Save");public string EmptyText=>T("Empty");public string CancelText=>T("Cancel");public string PasteText=>T("Paste");public string AddToLibraryText=>T("AddToLibrary");public string PreviewText=>T("Preview");public string SettingsSubtitle=>T("SettingsSubtitle");public string LanguageText=>T("Language");public string AppearanceText=>T("Appearance");public string MediaPreferencesText=>T("MediaPreferences");public string FormatText=>T("Format");public string QualityText=>T("Quality");public string EngineText=>T("Engine");public string CheckEngineText=>T("CheckEngine");public string AboutText=>T("About");public string CheckUpdatesText=>T("CheckUpdates");public string SystemText=>T("System");public string LightText=>T("Light");public string DarkText=>T("Dark");public string DownloadHint=>T("DownloadHint");public string PreviewHint=>T("PreviewHint");
    public string EngineSummary=>$"yt-dlp: {(HasTool("yt-dlp")?"ready":"missing")}  •  FFmpeg: {(HasTool("ffmpeg")?"ready":"missing")}  •  background-only child processes";
    public event PropertyChangedEventHandler? PropertyChanged;void OnPropertyChanged([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));

    async Task LoadStateAsync(){try{if(File.Exists(stateFile)){await using var s=File.OpenRead(stateFile);var st=await JsonSerializer.DeserializeAsync<StoredState>(s);if(st!=null){language=st.Language??"auto";theme=st.Theme??"System";defaultFormat=st.DefaultFormat??"MP3";defaultBitrate=st.DefaultBitrate==0?128:st.DefaultBitrate;foreach(var r in st.RootFolders??[])rootFolders.Add(r);foreach(var p in st.Pending??[])pending.Add(p);}}}catch{}ApplyLanguage();ApplyTheme(theme);if(rootFolders.Count>0)SelectedRoot=rootFolders[0];else RefreshItems();}
    async Task SaveStateAsync(){try{Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);var st=new StoredState{Language=language,Theme=theme,DefaultFormat=defaultFormat,DefaultBitrate=defaultBitrate,RootFolders=rootFolders.ToList(),Pending=pending.ToList()};var tmp=stateFile+".tmp";await using(var s=File.Create(tmp))await JsonSerializer.SerializeAsync(s,st,new JsonSerializerOptions{WriteIndented=true});File.Move(tmp,stateFile,true);}catch{}}
    void ApplyLanguage(){OnPropertyChanged(null);FlowDirection=(EffectiveLanguage()=="he"||EffectiveLanguage()=="ar")?FlowDirection.RightToLeft:FlowDirection.LeftToRight;}
    void ApplyTheme(string value){var dark=value=="Dark";if(value=="System")dark=SystemParameters.HighContrast;var r=Application.Current.Resources;r["AppBg"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#17181C":"#F7F8FB"));r["Surface"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#22242A":"#FFFFFF"));r["Surface2"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#2C2F36":"#F2F4F8"));r["Border"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#3A3D45":"#E1E5EC"));r["Text"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#F4F5F8":"#1A2230"));r["Muted"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#A9AFBC":"#667185"));OnPropertyChanged(nameof(SystemTheme));OnPropertyChanged(nameof(LightTheme));OnPropertyChanged(nameof(DarkTheme));}

    public void AddRoot(){using var d=new Forms.FolderBrowserDialog{Description="Choose a root folder for DownTrack."};if(d.ShowDialog()!=Forms.DialogResult.OK)return;if(rootFolders.Any(x=>x.Equals(d.SelectedPath,StringComparison.OrdinalIgnoreCase)))return;rootFolders.Add(d.SelectedPath);SelectedRoot=d.SelectedPath;_=SaveStateAsync();}
    public void OpenAddMedia(){if(string.IsNullOrWhiteSpace(SelectedRoot)){AddRoot();if(string.IsNullOrWhiteSpace(SelectedRoot))return;}DownloadVisible=true;OnPropertyChanged(nameof(DownloadVisibility));}
    void CloseAddMedia(){DownloadVisible=false;OnPropertyChanged(nameof(DownloadVisibility));}
    public void AddFolder(){if(string.IsNullOrWhiteSpace(SelectedRoot))return;var name=Microsoft.VisualBasic.Interaction.InputBox("Folder name","DownTrack","New Folder");if(string.IsNullOrWhiteSpace(name))return;pending.Add(new PendingChange{Type=PendingType.AddFolder,ParentPath=SelectedRoot,DisplayName=name.Trim()});UpdatePending();}
    public void RenameSelected(){if(SelectedItem==null||string.IsNullOrWhiteSpace(SelectedRoot)||string.IsNullOrWhiteSpace(SelectedItem.Path))return;var name=Microsoft.VisualBasic.Interaction.InputBox("New name","DownTrack",SelectedItem.Name);if(string.IsNullOrWhiteSpace(name)||name==SelectedItem.Name)return;pending.Add(new PendingChange{Type=PendingType.Rename,ParentPath=SelectedRoot,DisplayName=Path.GetFileName(SelectedItem.Path),NewName=name.Trim()+Path.GetExtension(SelectedItem.Path)});UpdatePending();}
    public void DeleteSelected(){if(SelectedItem==null||string.IsNullOrWhiteSpace(SelectedRoot)||string.IsNullOrWhiteSpace(SelectedItem.Path))return;if(MessageBox.Show($"Stage delete of \"{SelectedItem.Name}\"?","DownTrack",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;pending.Add(new PendingChange{Type=PendingType.Delete,ParentPath=SelectedRoot,DisplayName=Path.GetFileName(SelectedItem.Path)});UpdatePending();}
    public async Task ApplyPendingAsync(){foreach(var p in pending.ToList()){try{p.Status="Working";OnPropertyChanged(null);if(p.Type==PendingType.AddFolder)Directory.CreateDirectory(Path.Combine(p.ParentPath,p.DisplayName));if(p.Type==PendingType.Rename)File.Move(Path.Combine(p.ParentPath,p.DisplayName),Path.Combine(p.ParentPath,p.NewName!));if(p.Type==PendingType.Delete){var t=Path.Combine(p.ParentPath,p.DisplayName);if(File.Exists(t))File.Delete(t);else if(Directory.Exists(t))Directory.Delete(t,true);}if(p.Type==PendingType.AddMedia)await DownloadAsync(p);pending.Remove(p);UpdatePending();}catch(Exception ex){p.Status="Error";MessageBox.Show(ex.Message,"DownTrack",MessageBoxButton.OK,MessageBoxImage.Error);}}RefreshItems();await SaveStateAsync();}

    async Task DownloadAsync(PendingChange p){var exe=Tool("yt-dlp");if(!File.Exists(exe))throw new FileNotFoundException("yt-dlp.exe is missing from the tools folder.");Directory.CreateDirectory(p.ParentPath);var args=p.Format=="MP4"?$"-P \"{p.ParentPath}\" -o \"{Sanitize(p.DisplayName)}.%(ext)s\" --newline --no-playlist --merge-output-format mp4 \"{p.SourceUrl}\"":$"-P \"{p.ParentPath}\" -o \"{Sanitize(p.DisplayName)}.%(ext)s\" --newline --no-playlist -x --audio-format mp3 --audio-quality {p.Bitrate}K \"{p.SourceUrl}\"";var psi=new ProcessStartInfo(exe,args){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=AppContext.BaseDirectory};using var proc=new Process{StartInfo=psi};proc.Start();var re=new Regex(@"(\d+(?:\.\d+)?)%");while(!proc.StandardOutput.EndOfStream){var line=await proc.StandardOutput.ReadLineAsync();if(line==null)continue;var m=re.Match(line);if(m.Success&&double.TryParse(m.Groups[1].Value,out var pct)){p.Progress=Math.Clamp(pct,0,100);OnPropertyChanged(nameof(PendingSummary));}}await proc.WaitForExitAsync();if(proc.ExitCode!=0)throw new InvalidOperationException((await proc.StandardError.ReadToEndAsync()).Trim());p.Progress=100;}

    async void Url_Changed(object sender,System.Windows.Controls.TextChangedEventArgs e){inspectCts?.Cancel();inspectCts=new CancellationTokenSource();var token=inspectCts.Token;var url=UrlBox.Text.Trim();if(url.Length<12){PreviewMeta.Text=PreviewHint;return;}try{await Task.Delay(450,token);if(token.IsCancellationRequested)return;var exe=Tool("yt-dlp");if(!File.Exists(exe)){PreviewMeta.Text="yt-dlp is not installed in the tools folder.";return;}var psi=new ProcessStartInfo(exe,$"--dump-single-json --skip-download --no-warnings --no-playlist \"{url}\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};using var proc=Process.Start(psi)??throw new InvalidOperationException();var json=await proc.StandardOutput.ReadToEndAsync();await proc.WaitForExitAsync(token);using var doc=JsonDocument.Parse(json);var r=doc.RootElement;NameBox.Text=r.TryGetProperty("title",out var t)?t.GetString()??"New YouTube media":"New YouTube media";var channel=r.TryGetProperty("channel",out var c)?c.GetString():"";var duration=r.TryGetProperty("duration_string",out var ds)?ds.GetString():"";PreviewMeta.Text=$"{channel}  •  {duration}".Trim(' ','•');}catch(OperationCanceledException){}catch{PreviewMeta.Text="Waiting for a valid YouTube link…";}}

    void AddToLibrary_Click(object sender,RoutedEventArgs e){var url=UrlBox.Text.Trim();if(string.IsNullOrWhiteSpace(url)||string.IsNullOrWhiteSpace(SelectedRoot))return;var bitrate=int.Parse(((System.Windows.Controls.ComboBoxItem)Quality.SelectedItem).Content.ToString()!.Split(' ')[0]);pending.Add(new PendingChange{Type=PendingType.AddMedia,ParentPath=SelectedRoot,DisplayName=string.IsNullOrWhiteSpace(NameBox.Text)?"New YouTube media":NameBox.Text.Trim(),SourceUrl=url,Format=Mp4.IsChecked==true?"MP4":"MP3",Bitrate=bitrate});UpdatePending();CloseAddMedia();RefreshItems();UrlBox.Clear();NameBox.Text="New YouTube media";PreviewMeta.Text=PreviewHint;}

    void UpdatePending(){OnPropertyChanged(nameof(PendingVisibility));OnPropertyChanged(nameof(PendingSummary));_=SaveStateAsync();RefreshItems();}
    void RefreshItems(){items.Clear();if(string.IsNullOrWhiteSpace(SelectedRoot)||!Directory.Exists(SelectedRoot))return;try{foreach(var file in Directory.EnumerateFiles(SelectedRoot).Where(x=>filter=="All"||Path.GetExtension(x).TrimStart('.').Equals(filter,StringComparison.OrdinalIgnoreCase)).Where(x=>string.IsNullOrWhiteSpace(search)||Path.GetFileName(x).Contains(search,StringComparison.OrdinalIgnoreCase))){var i=new FileInfo(file);var ext=Path.GetExtension(file).TrimStart('.').ToUpperInvariant();items.Add(new MediaItem(Path.GetFileNameWithoutExtension(file),file,ext,i.Length));}foreach(var p in pending.Where(x=>x.Type==PendingType.AddMedia&&x.ParentPath.Equals(SelectedRoot,StringComparison.OrdinalIgnoreCase)))items.Add(new MediaItem(p.DisplayName,"",p.Format,0){Status="Waiting",Bitrate=p.Bitrate});}catch{}}

    bool HasTool(string n)=>File.Exists(Tool(n));string Tool(string n)=>Path.Combine(toolsFolder,n+".exe");static string Sanitize(string name){foreach(var c in Path.GetInvalidFileNameChars())name=name.Replace(c,'_');return string.IsNullOrWhiteSpace(name)?"DownTrack Media":name;}

    void Settings_Click(object sender,RoutedEventArgs e)=>Page="Settings";void Home_Click(object sender,RoutedEventArgs e)=>Page="Library";void AddRoot_Click(object sender,RoutedEventArgs e)=>AddRoot();void AddMedia_Click(object sender,RoutedEventArgs e)=>OpenAddMedia();void AddFolder_Click(object sender,RoutedEventArgs e)=>AddFolder();void Rename_Click(object sender,RoutedEventArgs e)=>RenameSelected();void Delete_Click(object sender,RoutedEventArgs e)=>DeleteSelected();void Back_Click(object sender,RoutedEventArgs e){}void Forward_Click(object sender,RoutedEventArgs e){}void Refresh_Click(object sender,RoutedEventArgs e)=>RefreshItems();void All_Click(object sender,RoutedEventArgs e)=>Filter="All";void Mp3_Click(object sender,RoutedEventArgs e)=>Filter="MP3";void Mp4_Click(object sender,RoutedEventArgs e)=>Filter="MP4";async void Save_Click(object sender,RoutedEventArgs e)=>await ApplyPendingAsync();void Paste_Click(object sender,RoutedEventArgs e){if(Clipboard.ContainsText())UrlBox.Text=Clipboard.GetText();}void CancelDownload_Click(object sender,RoutedEventArgs e)=>CloseAddMedia();void Engine_Click(object sender,RoutedEventArgs e)=>MessageBox.Show(EngineSummary,"DownTrack");void Updates_Click(object sender,RoutedEventArgs e)=>MessageBox.Show("Updater hook is ready for the release service.","DownTrack");

    static void ApplyMica(){try{var hwnd=new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow).Handle;int type=2;DwmSetWindowAttribute(hwnd,38,ref type,sizeof(int));int dark=0;DwmSetWindowAttribute(hwnd,20,ref dark,sizeof(int));}catch{}}
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);

    public record LanguageOption(string Code,string Name);
    public sealed class MediaItem : INotifyPropertyChanged
    {
        public string Name{get;}public string Path{get;}public string Format{get;}public long SizeBytes{get;}public int Bitrate{get;init;}public string Status{get;init;}="On disk";public string Duration=>"—";
        public string FormatLabel=>Bitrate>0?$"{Format} · {Bitrate} kbps":Format;public string SizeLabel=>SizeBytes<=0?"—":SizeBytes>1048576?$"{SizeBytes/1048576d:0.#} MB":$"{SizeBytes/1024d:0.#} KB";public Brush StatusBrush=>Status=="Waiting"?new SolidColorBrush(Color.FromRgb(255,240,210)):new SolidColorBrush(Color.FromRgb(221,246,234));public string StatusLabel=>Status;
        public MediaItem(string name,string path,string format,long size){Name=name;Path=path;Format=format;SizeBytes=size;}public event PropertyChangedEventHandler? PropertyChanged;
    }
    public enum PendingType{AddMedia,AddFolder,Rename,Delete}
    public sealed class PendingChange{public PendingType Type{get;set;}public string ParentPath{get;set;}="";public string DisplayName{get;set;}="";public string? NewName{get;set;}public string? SourceUrl{get;set;}public string Format{get;set;}="MP3";public int Bitrate{get;set;}=128;public string Status{get;set;}="Waiting";public double Progress{get;set;}}
    public sealed class StoredState{public string? Language{get;set;}public string? Theme{get;set;}public string? DefaultFormat{get;set;}public int DefaultBitrate{get;set;}=128;public List<string>? RootFolders{get;set;}public List<PendingChange>? Pending{get;set;}}
}

public static class StringExtensions
{
    public static T GetValueOrDefault<T>(this Dictionary<string,T> dict,string key,T fallback)=>dict.TryGetValue(key,out var v)?v:fallback;
}