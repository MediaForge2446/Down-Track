using System.Collections.ObjectModel;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MessageBox = System.Windows.MessageBox;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Forms = System.Windows.Forms;

namespace DownTrack;

public sealed partial class MainWindow : FluentWindow, INotifyPropertyChanged
{
    readonly string stateFile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DownTrack","state.json");
    readonly EngineSetupService engine=new();
    readonly SemaphoreSlim engineGate=new(1,1);
    readonly ObservableCollection<string> rootFolders=[];
    readonly ObservableCollection<MediaItem> items=[];readonly ObservableCollection<PendingChange> pending=[];
    readonly List<(string code,string name)> languages=[("auto","Automatic"),("en","English"),("he","עברית"),("es","Español"),("fr","Français"),("de","Deutsch"),("it","Italiano"),("pt","Português"),("nl","Nederlands"),("pl","Polski"),("cs","Čeština"),("tr","Türkçe"),("uk","Українська"),("ru","Русский"),("ar","العربية"),("el","Ελληνικά"),("ro","Română"),("ja","日本語"),("ko","한국어"),("zh-CN","简体中文"),("zh-TW","繁體中文")];
    readonly Dictionary<string,Dictionary<string,string>> t=new(){
        ["en"]=new(){["Library"]="Library",["LibrarySubtitle"]="Your folders, media and pending work in one place.",["AddRoot"]="Add root folder",["AddMedia"]="Add media",["AddFolder"]="Add folder",["Rename"]="Rename",["Delete"]="Delete",["Settings"]="Settings",["Home"]="Home",["All"]="All",["Search"]="Search files…",["Pending"]="Pending Changes",["Save"]="Save Changes",["Empty"]="This folder is empty",["Cancel"]="Cancel",["Paste"]="Paste",["AddToLibrary"]="Add to Library",["Preview"]="Media Preview",["SettingsSubtitle"]="Tune DownTrack to your workflow.",["Language"]="Language",["Appearance"]="Appearance",["MediaPreferences"]="Media preferences",["Format"]="Default format",["Quality"]="Audio quality",["Engine"]="Media engine",["CheckEngine"]="Check for Engine Updates",["About"]="About",["CheckUpdates"]="Check for Updates",["System"]="System",["Light"]="Light",["Dark"]="Dark",["DownloadHint"]="Paste a YouTube video or playlist link. Details load quietly.",["PreviewHint"]="Paste a valid link to fetch media details.",["Folders"]="Folders",["LibraryTools"]="Library tools",["RootFolders"]="Root folders",["RootFoldersSubtitle"]="Choose a folder to open its Explorer view.",["Open"]="Open",["EmptyLibrary"]="Your library is ready for its first folder.",["EmptyLibraryHint"]="Add a root folder, then add media without changing anything on disk until you save.",["Explorer"]="Explorer",["Name"]="Name",["FormatHeader"]="Format / bitrate",["SizeHeader"]="Size",["DurationHeader"]="Duration",["StatusHeader"]="Status"},
        ["he"]=new(){["Library"]="ספרייה",["LibrarySubtitle"]="התיקיות, המדיה והשינויים הממתינים שלך במקום אחד.",["AddRoot"]="הוסף תיקייה ראשית",["AddMedia"]="הוסף מדיה",["AddFolder"]="הוסף תיקייה",["Rename"]="שנה שם",["Delete"]="מחק",["Settings"]="הגדרות",["Home"]="בית",["All"]="הכול",["Search"]="חיפוש קבצים…",["Pending"]="שינויים ממתינים",["Save"]="שמור שינויים",["Empty"]="התיקייה ריקה",["Cancel"]="ביטול",["Paste"]="הדבק",["AddToLibrary"]="הוסף לספרייה",["Preview"]="תצוגת מדיה",["SettingsSubtitle"]="התאם את DownTrack לדרך העבודה שלך.",["Language"]="שפה",["Appearance"]="מראה",["MediaPreferences"]="העדפות מדיה",["Format"]="פורמט ברירת מחדל",["Quality"]="איכות שמע",["Engine"]="מנוע מדיה",["CheckEngine"]="בדוק עדכוני מנוע",["About"]="אודות",["CheckUpdates"]="בדוק עדכונים",["System"]="כמו המערכת",["Light"]="בהיר",["Dark"]="כהה",["DownloadHint"]="הדבק קישור מסרטון או פלייליסט ב-YouTube. הפרטים נטענים ברקע.",["PreviewHint"]="הדבק קישור תקין כדי לטעון פרטי מדיה.",["Folders"]="תיקיות",["LibraryTools"]="כלי הספרייה",["RootFolders"]="תיקיות ראשיות",["RootFoldersSubtitle"]="בחר תיקייה כדי לפתוח את הסייר שלה.",["Open"]="פתח",["EmptyLibrary"]="הספרייה מוכנה לתיקייה הראשונה שלך.",["EmptyLibraryHint"]="הוסף תיקייה ראשית, ואז מדיה בלי לשנות דבר בדיסק עד לשמירת השינויים.",["Explorer"]="סייר",["Name"]="שם",["FormatHeader"]="פורמט / ביטרייט",["SizeHeader"]="גודל",["DurationHeader"]="משך",["StatusHeader"]="סטטוס"},
        ["es"]=new(){["Library"]="Biblioteca",["LibrarySubtitle"]="Tus carpetas, medios y cambios pendientes en un solo lugar.",["AddRoot"]="Añadir carpeta raíz",["AddMedia"]="Añadir multimedia",["AddFolder"]="Añadir carpeta",["Rename"]="Cambiar nombre",["Delete"]="Eliminar",["Settings"]="Configuración",["Home"]="Inicio",["All"]="Todo",["Search"]="Buscar archivos…",["Pending"]="Cambios pendientes",["Save"]="Guardar cambios",["Empty"]="Esta carpeta está vacía",["Cancel"]="Cancelar",["Paste"]="Pegar",["AddToLibrary"]="Añadir a la biblioteca",["Preview"]="Vista previa",["SettingsSubtitle"]="Adapta DownTrack a tu flujo de trabajo.",["Language"]="Idioma",["Appearance"]="Apariencia",["MediaPreferences"]="Preferencias multimedia",["Format"]="Formato predeterminado",["Quality"]="Calidad de audio",["Engine"]="Motor multimedia",["CheckEngine"]="Buscar actualizaciones del motor",["About"]="Acerca de",["CheckUpdates"]="Buscar actualizaciones",["System"]="Sistema",["Light"]="Claro",["Dark"]="Oscuro",["DownloadHint"]="Pega un enlace de YouTube. Los detalles se cargan en segundo plano.",["PreviewHint"]="Pega un enlace válido para cargar los detalles.",["Folders"]="Carpetas",["LibraryTools"]="Herramientas",["RootFolders"]="Carpetas raíz",["RootFoldersSubtitle"]="Elige una carpeta para abrir su Explorador.",["Open"]="Abrir",["EmptyLibrary"]="Tu biblioteca está lista.",["EmptyLibraryHint"]="Añade una carpeta raíz y después tus medios; nada se escribe hasta guardar.",["Explorer"]="Explorador",["Name"]="Nombre",["FormatHeader"]="Formato / bitrate",["SizeHeader"]="Tamaño",["DurationHeader"]="Duración",["StatusHeader"]="Estado"}
    };
    readonly List<string> history=[];int historyIndex=-1;bool navigating;int toastVersion;CancellationTokenSource? inspectCts;
    string language="auto",theme="System",filter="All",search="",defaultFormat="MP3";int defaultBitrate=128;string? selectedRoot;MediaItem? selectedItem;string page="Library";string toastText="";bool downloadVisible;bool previewPlaylist;

    public MainWindow(){InitializeComponent();DataContext=this;Loaded+=async(_,_)=>{WindowState=WindowState.Maximized;await LoadTranslationsAsync();await LoadStateAsync();_=EnsureMediaEngineQuietAsync();};}

    public ObservableCollection<string> RootFolders=>rootFolders;public ObservableCollection<MediaItem> Items=>items;public ObservableCollection<PendingChange> PendingChanges=>pending;
    public string? SelectedRoot{get=>selectedRoot;set{if(value==selectedRoot)return;selectedRoot=value;Breadcrumb=value??"Library";if(!navigating&&value!=null){if(historyIndex<history.Count-1)history.RemoveRange(historyIndex+1,history.Count-historyIndex-1);history.Add(value);historyIndex=history.Count-1;}OnPropertyChanged();OnPropertyChanged(nameof(Breadcrumb));RefreshItems();}}
    public MediaItem? SelectedItem{get=>selectedItem;set{selectedItem=value;OnPropertyChanged();}}
    public string Search{get=>search;set{search=value;OnPropertyChanged();RefreshItems();}}public string Filter{get=>filter;set{filter=value;OnPropertyChanged();RefreshItems();}}
    public string Breadcrumb{get;private set;}="Library";public string Page{get=>page;private set{page=value;OnPropertyChanged();OnPropertyChanged(nameof(LibraryVisibility));OnPropertyChanged(nameof(ExplorerVisibility));OnPropertyChanged(nameof(SettingsVisibility));}}
    public Visibility LibraryVisibility=>Page=="Library"?Visibility.Visible:Visibility.Collapsed;public Visibility ExplorerVisibility=>Page=="Explorer"?Visibility.Visible:Visibility.Collapsed;public Visibility SettingsVisibility=>Page=="Settings"?Visibility.Visible:Visibility.Collapsed;
    public bool DownloadVisible{get=>downloadVisible;private set{downloadVisible=value;OnPropertyChanged(nameof(DownloadVisibility));}}public Visibility DownloadVisibility=>DownloadVisible?Visibility.Visible:Visibility.Collapsed;
    public Visibility PendingVisibility=>pending.Count>0?Visibility.Visible:Visibility.Collapsed;public Visibility EmptyItemsVisibility=>items.Count==0?Visibility.Visible:Visibility.Collapsed;public Visibility RootEmptyVisibility=>rootFolders.Count==0?Visibility.Visible:Visibility.Collapsed;
    public Visibility ToastVisibility=>string.IsNullOrWhiteSpace(toastText)?Visibility.Collapsed:Visibility.Visible;public string ToastText=>toastText;public string PendingSummary=>pending.Count==1?"1 change is waiting to be saved to disk.":$"{pending.Count} changes are waiting to be saved to disk.";
    public string LanguageCode{get=>language;set{language=value;ApplyLanguage();_=SaveStateAsync();}}public string Theme{get=>theme;set{theme=value;ApplyTheme(value);OnPropertyChanged();_=SaveStateAsync();}}
    public bool SystemTheme{get=>Theme=="System";set{if(value)Theme="System";}}public bool LightTheme{get=>Theme=="Light";set{if(value)Theme="Light";}}public bool DarkTheme{get=>Theme=="Dark";set{if(value)Theme="Dark";}}
    public string DefaultFormat{get=>defaultFormat;set{defaultFormat=value;OnPropertyChanged();_=SaveStateAsync();}}public int DefaultBitrate{get=>defaultBitrate;set{defaultBitrate=value;OnPropertyChanged();_=SaveStateAsync();}}
    public IReadOnlyList<LanguageOption> Languages=>languages.Select(x=>new LanguageOption(x.code,x.name)).ToList();public LanguageOption? SelectedLanguage{get{var x=languages.FirstOrDefault(x=>x.code==language);return new LanguageOption(x.code,x.name);}set{if(value!=null)LanguageCode=value.Code;}}
    public IReadOnlyList<string> Formats=>["MP3","MP4"];public IReadOnlyList<int> Bitrates=>[128,192,256,320];

    string EffectiveLanguage(){if(language!="auto")return language;var ui=CultureInfo.CurrentUICulture.Name;return ui.StartsWith("he")?"he":ui.StartsWith("es")?"es":"en";}string T(string key)=>t.TryGetValue(EffectiveLanguage(),out var d)&&d.TryGetValue(key,out var v)?v:t["en"].GetValueOrDefault(key,key);
    public string HomeText=>T("Home");public string LibraryTitle=>T("Library");public string LibrarySubtitle=>T("LibrarySubtitle");public string AddRootText=>T("AddRoot");public string AddMediaText=>T("AddMedia");public string AddFolderText=>T("AddFolder");public string RenameText=>T("Rename");public string DeleteText=>T("Delete");public string SettingsText=>T("Settings");public string SearchText=>T("Search");public string PendingText=>T("Pending");public string SaveText=>T("Save");public string EmptyText=>T("Empty");public string CancelText=>T("Cancel");public string PasteText=>T("Paste");public string AddToLibraryText=>T("AddToLibrary");public string PreviewText=>T("Preview");public string SettingsSubtitle=>T("SettingsSubtitle");public string LanguageText=>T("Language");public string AppearanceText=>T("Appearance");public string MediaPreferencesText=>T("MediaPreferences");public string FormatText=>T("Format");public string QualityText=>T("Quality");public string EngineText=>T("Engine");public string CheckEngineText=>T("CheckEngine");public string AboutText=>T("About");public string CheckUpdatesText=>T("CheckUpdates");public string SystemText=>T("System");public string LightText=>T("Light");public string DarkText=>T("Dark");public string DownloadHint=>T("DownloadHint");public string PreviewHint=>T("PreviewHint");public string RootEmptyHint=>rootFolders.Count==0?T("EmptyLibraryHint"):"";public string FoldersText=>T("Folders");public string LibraryToolsText=>T("LibraryTools");public string RootFoldersTitle=>T("RootFolders");public string RootFoldersSubtitle=>T("RootFoldersSubtitle");public string OpenText=>T("Open");public string EmptyLibraryText=>T("EmptyLibrary");public string EmptyLibraryHint=>T("EmptyLibraryHint");public string ExplorerTitle=>string.IsNullOrWhiteSpace(selectedRoot)?T("Explorer"):System.IO.Path.GetFileName(selectedRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar,System.IO.Path.AltDirectorySeparatorChar));public string AllText=>T("All");public string NameHeader=>T("Name");public string FormatHeader=>T("FormatHeader");public string SizeHeader=>T("SizeHeader");public string DurationHeader=>T("DurationHeader");public string StatusHeader=>T("StatusHeader");
    public string EngineSummary=>$"yt-dlp: {(HasTool("yt-dlp")?"ready":"missing")}  •  FFmpeg: {(HasTool("ffmpeg")&&HasTool("ffprobe")?"ready":"missing")}  •  Deno: {(HasTool("deno")?"ready":"missing")}  •  hidden background child processes";
    public event PropertyChangedEventHandler? PropertyChanged;void OnPropertyChanged([CallerMemberName]string? n=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(n));

    async Task LoadTranslationsAsync()
    {
        try
        {
            var folder=Path.Combine(AppContext.BaseDirectory,"Locales");
            if(!Directory.Exists(folder)) return;
            foreach(var file in Directory.EnumerateFiles(folder,"*.json"))
            {
                var code=Path.GetFileNameWithoutExtension(file);
                await using var stream=File.OpenRead(file);
                var dict=await JsonSerializer.DeserializeAsync<Dictionary<string,string>>(stream);
                if(dict!=null) t[code]=dict;
            }
        }
        catch { }
    }

    async Task LoadStateAsync(){try{if(File.Exists(stateFile)){await using var s=File.OpenRead(stateFile);var st=await JsonSerializer.DeserializeAsync<StoredState>(s);if(st!=null){language=st.Language??"auto";theme=st.Theme??"System";defaultFormat=st.DefaultFormat??"MP3";defaultBitrate=st.DefaultBitrate==0?128:st.DefaultBitrate;foreach(var r in st.RootFolders??[])rootFolders.Add(r);foreach(var p in st.Pending??[])pending.Add(p);}}}catch{}ApplyLanguage();ApplyTheme(theme);if(rootFolders.Count>0){navigating=true;SelectedRoot=rootFolders[0];navigating=false;history.Clear();history.Add(SelectedRoot!);historyIndex=0;Page="Library";}RefreshItems();}
    async Task SaveStateAsync(){try{Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);var st=new StoredState{Language=language,Theme=theme,DefaultFormat=defaultFormat,DefaultBitrate=defaultBitrate,RootFolders=rootFolders.ToList(),Pending=pending.ToList()};var tmp=stateFile+".tmp";await using(var s=File.Create(tmp))await JsonSerializer.SerializeAsync(s,st,new JsonSerializerOptions{WriteIndented=true});File.Move(tmp,stateFile,true);}catch{}}
    void ApplyLanguage(){OnPropertyChanged(null);FlowDirection=EffectiveLanguage() is "he" or "ar"?System.Windows.FlowDirection.RightToLeft:System.Windows.FlowDirection.LeftToRight;}
    void ApplyTheme(string value)
    {
        var dark=value=="Dark";

        if(value=="System")
        {
            ApplicationThemeManager.ApplySystemTheme();
            try
            {
                dark=((int?)Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme",1)??1)==0;
            }
            catch
            {
                dark=SystemParameters.HighContrast;
            }
        }
        else
        {
            ApplicationThemeManager.Apply(dark?ApplicationTheme.Dark:ApplicationTheme.Light);
        }

        var r=System.Windows.Application.Current.Resources;
        r["AppBg"]=Brush(dark?"#17181C":"#F7F8FB");
        r["Surface"]=Brush(dark?"#22242A":"#FFFFFF");
        r["Surface2"]=Brush(dark?"#2C2F36":"#F2F4F8");
        r["Border"]=Brush(dark?"#3A3D45":"#E1E5EC");
        r["Text"]=Brush(dark?"#F4F5F8":"#1A2230");
        r["Muted"]=Brush(dark?"#A9AFBC":"#667185");
        r["SideBarBackground"]=Brush(dark?"#202227":"#F1F3F7");
        r["SearchBackground"]=Brush(dark?"#2A2D33":"#F4F5F8");
        r["IconBackground"]=Brush(dark?"#30343C":"#ECEEF5");
        r["AccentSoft"]=Brush(dark?"#323255":"#EAEAFF");
        r["PendingBackground"]=Brush(dark?"#352B18":"#FFF8EA");
        r["PendingBorder"]=Brush(dark?"#6A5526":"#F0D49F");
        r["PendingText"]=Brush(dark?"#F4C86A":"#9A6700");
        OnPropertyChanged(nameof(SystemTheme));
        OnPropertyChanged(nameof(LightTheme));
        OnPropertyChanged(nameof(DarkTheme));
        OnPropertyChanged(null);
    }
    static System.Windows.Media.SolidColorBrush Brush(string hex)=>
        (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFrom(hex)!;
        void OpenRoot(string path){SelectedRoot=path;Page="Explorer";OnPropertyChanged(nameof(ExplorerTitle));}
    public void AddRoot(){using var d=new Forms.FolderBrowserDialog{Description="Choose a root folder for DownTrack."};if(d.ShowDialog()!=Forms.DialogResult.OK)return;if(rootFolders.Any(x=>x.Equals(d.SelectedPath,StringComparison.OrdinalIgnoreCase)))return;rootFolders.Add(d.SelectedPath);OpenRoot(d.SelectedPath);_=SaveStateAsync();ShowToast("Root folder added to your library.");}
    void OpenAddMedia(){if(string.IsNullOrWhiteSpace(SelectedRoot)){AddRoot();if(string.IsNullOrWhiteSpace(SelectedRoot))return;}DownloadVisible=true;OnPropertyChanged(nameof(DefaultBitrate));Quality.SelectedIndex=Math.Max(0,Array.IndexOf(new[]{128,192,256,320},defaultBitrate));Mp3.IsChecked=defaultFormat=="MP3";Mp4.IsChecked=defaultFormat=="MP4";}
    void CloseAddMedia(){DownloadVisible=false;inspectCts?.Cancel();}
    void AddFolder(){if(string.IsNullOrWhiteSpace(SelectedRoot))return;var name=Microsoft.VisualBasic.Interaction.InputBox("Folder name","DownTrack","New Folder");if(string.IsNullOrWhiteSpace(name))return;pending.Add(new PendingChange{Type=PendingType.AddFolder,ParentPath=SelectedRoot,DisplayName=name.Trim()});UpdatePending();ShowToast("Folder staged. It will be created when you save changes.");}
    void RenameSelected(){if(SelectedItem==null||string.IsNullOrWhiteSpace(SelectedRoot)||string.IsNullOrWhiteSpace(SelectedItem.Path))return;var name=Microsoft.VisualBasic.Interaction.InputBox("New name","DownTrack",SelectedItem.Name);if(string.IsNullOrWhiteSpace(name)||name==SelectedItem.Name)return;pending.Add(new PendingChange{Type=PendingType.Rename,ParentPath=SelectedRoot,DisplayName=System.IO.Path.GetFileName(SelectedItem.Path),NewName=name.Trim()+System.IO.Path.GetExtension(SelectedItem.Path)});UpdatePending();}
    void DeleteSelected(){if(SelectedItem==null||string.IsNullOrWhiteSpace(SelectedRoot)||string.IsNullOrWhiteSpace(SelectedItem.Path))return;if(System.Windows.MessageBox.Show($"Stage delete of \"{SelectedItem.Name}\"?","DownTrack",System.Windows.MessageBoxButton.YesNo,MessageBoxImage.Warning)!=System.Windows.MessageBoxResult.Yes)return;pending.Add(new PendingChange{Type=PendingType.Delete,ParentPath=SelectedRoot,DisplayName=System.IO.Path.GetFileName(SelectedItem.Path)});UpdatePending();}
    public async Task ApplyPendingAsync(){foreach(var p in pending.ToList()){try{p.Status="Working";p.Progress=0;OnPropertyChanged(nameof(PendingChanges));if(p.Type==PendingType.AddFolder){Directory.CreateDirectory(System.IO.Path.Combine(p.ParentPath,p.DisplayName));p.Progress=100;}else if(p.Type==PendingType.Rename){File.Move(System.IO.Path.Combine(p.ParentPath,p.DisplayName),System.IO.Path.Combine(p.ParentPath,p.NewName!));p.Progress=100;}else if(p.Type==PendingType.Delete){var x=System.IO.Path.Combine(p.ParentPath,p.DisplayName);if(File.Exists(x))File.Delete(x);else if(Directory.Exists(x))Directory.Delete(x,true);p.Progress=100;}else if(p.Type==PendingType.AddMedia){if(!await EnsureMediaEngineQuietAsync(true)){p.Status="Error";p.Progress=0;p.OnChanged();ShowToast("Media tools are not ready. Open Settings to check the media engine.");continue;}await DownloadAsync(p);}pending.Remove(p);UpdatePending();ShowToast("Change applied to disk.");}catch(Exception ex){p.Status="Error";p.OnChanged();OnPropertyChanged(nameof(PendingChanges));ShowToast(ex.Message);}}RefreshItems();await SaveStateAsync();}
    void UpdatePending(){OnPropertyChanged(nameof(PendingVisibility));OnPropertyChanged(nameof(PendingSummary));OnPropertyChanged(nameof(EmptyItemsVisibility));_=SaveStateAsync();RefreshItems();}

    async Task DownloadAsync(PendingChange p){var exe=Tool("yt-dlp");if(!File.Exists(exe))throw new FileNotFoundException("yt-dlp.exe is missing from the tools folder.");if(!File.Exists(Tool("ffmpeg")))throw new FileNotFoundException("ffmpeg.exe is missing from the media engine folder.");if(!File.Exists(Tool("deno")))throw new FileNotFoundException("deno.exe is missing from the media engine folder.");Directory.CreateDirectory(p.ParentPath);
        var psi=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=AppContext.BaseDirectory};
        psi.ArgumentList.Add("-P");psi.ArgumentList.Add(p.ParentPath);psi.ArgumentList.Add("--newline");psi.ArgumentList.Add("--ffmpeg-location");psi.ArgumentList.Add(engine.EngineFolder);psi.ArgumentList.Add("--js-runtimes");psi.ArgumentList.Add("deno:"+Tool("deno"));psi.ArgumentList.Add("--remote-components");psi.ArgumentList.Add("ejs:github");
        if(p.IsPlaylist){psi.ArgumentList.Add("-o");psi.ArgumentList.Add("%(playlist_index)03d - %(title)s.%(ext)s");psi.ArgumentList.Add("--yes-playlist");}else{psi.ArgumentList.Add("-o");psi.ArgumentList.Add(Sanitize(p.DisplayName)+".%(ext)s");psi.ArgumentList.Add("--no-playlist");}
        if(p.Format=="MP4"){psi.ArgumentList.Add("--merge-output-format");psi.ArgumentList.Add("mp4");}else{psi.ArgumentList.Add("-x");psi.ArgumentList.Add("--audio-format");psi.ArgumentList.Add("mp3");psi.ArgumentList.Add("--audio-quality");psi.ArgumentList.Add(p.Bitrate+"K");}
        psi.ArgumentList.Add(p.SourceUrl??"");
        using var proc=new Process{StartInfo=psi};proc.Start();var re=new Regex(@"(\d+(?:\.\d+)?)%");while(!proc.StandardOutput.EndOfStream){var line=await proc.StandardOutput.ReadLineAsync();if(line==null)continue;var m=re.Match(line);if(m.Success&&double.TryParse(m.Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var pct)){p.Progress=Math.Clamp(pct,0,100);p.OnChanged();}}
        await proc.WaitForExitAsync();if(proc.ExitCode!=0){var err=(await proc.StandardError.ReadToEndAsync()).Trim();throw new InvalidOperationException(string.IsNullOrWhiteSpace(err)?"yt-dlp failed.":err);}p.Progress=100;p.Status="Done";p.OnChanged();}

    async Task<bool> EnsureMediaEngineQuietAsync(bool repair=false)
    {
        await engineGate.WaitAsync();
        try
        {
            if(HasAllTools())
            {
                OnPropertyChanged(nameof(EngineSummary));
                return true;
            }

            if(!repair)
            {
                OnPropertyChanged(nameof(EngineSummary));
                return false;
            }

            await engine.EnsureAsync(null,null,true);
            OnPropertyChanged(nameof(EngineSummary));
            return HasAllTools();
                catch(Exception ex)
        {
            OnPropertyChanged(nameof(EngineSummary));
            if(repair)
            {
                ShowToast("Media engine could not be updated. Check your connection and try again.");
            }
            Debug.WriteLine("Media engine setup failed: " + ex);
            return false;
        }
        finally
        {
            engineGate.Release();
        }
    }

    async void Url_Changed(object sender,System.Windows.Controls.TextChangedEventArgs e){inspectCts?.Cancel();inspectCts=new CancellationTokenSource();var token=inspectCts.Token;var url=UrlBox.Text.Trim();if(url.Length<12){PreviewMeta.Text=PreviewHint;return;}try{await Task.Delay(450,token);if(!await EnsureMediaEngineQuietAsync()){PreviewMeta.Text="Media tools are not ready. Check Settings.";return;}var exe=Tool("yt-dlp");if(!File.Exists(exe)){PreviewMeta.Text="Media tools are not ready. Check Settings.";return;}var psi=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};psi.ArgumentList.Add("--dump-single-json");psi.ArgumentList.Add("--skip-download");psi.ArgumentList.Add("--no-warnings");psi.ArgumentList.Add("--playlist-end");psi.ArgumentList.Add("1");psi.ArgumentList.Add(url);using var proc=Process.Start(psi)??throw new InvalidOperationException();var json=await proc.StandardOutput.ReadToEndAsync();await proc.WaitForExitAsync(token);using var doc=JsonDocument.Parse(json);var r=doc.RootElement;NameBox.Text=r.TryGetProperty("title",out var tt)?tt.GetString()??"New YouTube media":"New YouTube media";previewPlaylist=r.TryGetProperty("playlist_count",out var pc)&&pc.ValueKind==JsonValueKind.Number&&pc.GetInt32()>1;var ch=r.TryGetProperty("channel",out var c)?c.GetString():"";var dur=r.TryGetProperty("duration_string",out var ds)?ds.GetString():"";PreviewMeta.Text=$"{ch}  •  {dur}".Trim(' ','•');if(r.TryGetProperty("thumbnail",out var th)&&th.GetString() is {Length:>0} image)await LoadThumbnailAsync(image,token);}catch(OperationCanceledException){}catch{PreviewMeta.Text="Waiting for a valid YouTube link…";}}
    async Task LoadThumbnailAsync(string url,CancellationToken token){try{using var client=new HttpClient();var bytes=await client.GetByteArrayAsync(url,token);using var ms=new MemoryStream(bytes);var bmp=new BitmapImage();bmp.BeginInit();bmp.CacheOption=BitmapCacheOption.OnLoad;bmp.StreamSource=ms;bmp.EndInit();bmp.Freeze();PreviewImage.Source=bmp;}catch{}}

    void AddToLibrary_Click(object sender,RoutedEventArgs e){var url=UrlBox.Text.Trim();if(string.IsNullOrWhiteSpace(url)||string.IsNullOrWhiteSpace(SelectedRoot))return;var bitrate=int.Parse(((System.Windows.Controls.ComboBoxItem)Quality.SelectedItem).Content.ToString()!.Split(' ')[0]);pending.Add(new PendingChange{Type=PendingType.AddMedia,ParentPath=SelectedRoot,DisplayName=string.IsNullOrWhiteSpace(NameBox.Text)?"New YouTube media":NameBox.Text.Trim(),SourceUrl=url,Format=Mp4.IsChecked==true?"MP4":"MP3",Bitrate=bitrate,IsPlaylist=previewPlaylist});UpdatePending();CloseAddMedia();UrlBox.Clear();NameBox.Text="New YouTube media";PreviewMeta.Text=PreviewHint;PreviewImage.Source=null;ShowToast("Media added to Pending Changes. Nothing has been downloaded yet.");}

    void RefreshItems(){items.Clear();OnPropertyChanged(nameof(EmptyItemsVisibility));if(string.IsNullOrWhiteSpace(SelectedRoot)||!Directory.Exists(SelectedRoot))return;try{foreach(var file in Directory.EnumerateFiles(SelectedRoot).Where(x=>Filter=="All"||System.IO.Path.GetExtension(x).TrimStart('.').Equals(Filter,StringComparison.OrdinalIgnoreCase)).Where(x=>string.IsNullOrWhiteSpace(Search)||System.IO.Path.GetFileName(x).Contains(Search,StringComparison.OrdinalIgnoreCase))){var i=new FileInfo(file);items.Add(new MediaItem(System.IO.Path.GetFileNameWithoutExtension(file),file,System.IO.Path.GetExtension(file).TrimStart('.').ToUpperInvariant(),i.Length));}foreach(var p in pending.Where(x=>x.Type==PendingType.AddMedia&&x.ParentPath.Equals(SelectedRoot,StringComparison.OrdinalIgnoreCase)))items.Add(new MediaItem(p.DisplayName,"",p.Format,0){Status="Waiting",Bitrate=p.Bitrate});foreach(var p in pending.Where(x=>x.Type==PendingType.AddFolder&&x.ParentPath.Equals(SelectedRoot,StringComparison.OrdinalIgnoreCase)))items.Add(new MediaItem(p.DisplayName,"","Folder",0){Status="Waiting"});}catch{}OnPropertyChanged(nameof(EmptyItemsVisibility));}

    void ShowToast(string text){toastText=text;var v=++toastVersion;OnPropertyChanged(nameof(ToastText));OnPropertyChanged(nameof(ToastVisibility));_=HideToast(v);}
    async Task HideToast(int v){await Task.Delay(2600);if(v!=toastVersion)return;toastText="";OnPropertyChanged(nameof(ToastText));OnPropertyChanged(nameof(ToastVisibility));}
    bool HasTool(string n)=>File.Exists(Tool(n));bool HasAllTools()=>HasTool("yt-dlp")&&HasTool("ffmpeg")&&HasTool("ffprobe")&&HasTool("deno");string Tool(string n)=>System.IO.Path.Combine(engine.EngineFolder,n+".exe");static string Sanitize(string name){foreach(var c in System.IO.Path.GetInvalidFileNameChars())name=name.Replace(c,'_');return string.IsNullOrWhiteSpace(name)?"DownTrack Media":name;}

    void Settings_Click(object sender,RoutedEventArgs e)=>Page="Settings";void Home_Click(object sender,RoutedEventArgs e){Page="Library";SelectedItem=null;}void AddRoot_Click(object sender,RoutedEventArgs e)=>AddRoot();void AddMedia_Click(object sender,RoutedEventArgs e)=>OpenAddMedia();void AddFolder_Click(object sender,RoutedEventArgs e)=>AddFolder();void Rename_Click(object sender,RoutedEventArgs e)=>RenameSelected();void Delete_Click(object sender,RoutedEventArgs e)=>DeleteSelected();void Back_Click(object sender,RoutedEventArgs e){if(historyIndex<=0)return;historyIndex--;navigating=true;SelectedRoot=history[historyIndex];navigating=false;Page="Explorer";}void Forward_Click(object sender,RoutedEventArgs e){if(historyIndex>=history.Count-1)return;historyIndex++;navigating=true;SelectedRoot=history[historyIndex];navigating=false;Page="Explorer";}void Refresh_Click(object sender,RoutedEventArgs e)=>RefreshItems();void All_Click(object sender,RoutedEventArgs e)=>Filter="All";void Mp3_Click(object sender,RoutedEventArgs e)=>Filter="MP3";void Mp4_Click(object sender,RoutedEventArgs e)=>Filter="MP4";async void Save_Click(object sender,RoutedEventArgs e)=>await ApplyPendingAsync();void Paste_Click(object sender,RoutedEventArgs e){if(System.Windows.Clipboard.ContainsText())UrlBox.Text=System.Windows.Clipboard.GetText();}void CancelDownload_Click(object sender,RoutedEventArgs e)=>CloseAddMedia();void RootCard_Click(object sender,RoutedEventArgs e){if(sender is System.Windows.Controls.Button b&&b.Tag is string path)OpenRoot(path);}void RootList_SelectionChanged(object sender,System.Windows.Controls.SelectionChangedEventArgs e){if(SelectedRoot!=null&&Page!="Explorer"){Page="Explorer";OnPropertyChanged(nameof(ExplorerTitle));}}async void Engine_Click(object sender,RoutedEventArgs e){var ok=await EnsureMediaEngineQuietAsync(true);ShowToast(ok?"Media engine is ready.":"Media engine could not be updated. Check your connection and try again.");}void Updates_Click(object sender,RoutedEventArgs e)=>MessageBox.Show("Updater hook is ready for the release service.","DownTrack");

    static void ApplyMica(){try{var hwnd=new System.Windows.Interop.WindowInteropHelper(System.Windows.Application.Current.MainWindow).Handle;int type=2;DwmSetWindowAttribute(hwnd,38,ref type,sizeof(int));int dark=0;DwmSetWindowAttribute(hwnd,20,ref dark,sizeof(int));}catch{}}
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);

    public record LanguageOption(string Code,string Name);
    public sealed class MediaItem : INotifyPropertyChanged
    {public string Name{get;}public string Path{get;}public string Format{get;}public long SizeBytes{get;}public int Bitrate{get;init;}public string Status{get;init;}="On disk";public string Duration=>"—";public string FormatLabel=>Bitrate>0?$"{Format} · {Bitrate} kbps":Format;public string SizeLabel=>SizeBytes<=0?"—":SizeBytes>1048576?$"{SizeBytes/1048576d:0.#} MB":$"{SizeBytes/1024d:0.#} KB";public System.Windows.Media.Brush StatusBrush => (System.Windows.Media.Brush)System.Windows.Application.Current.Resources[Status=="Waiting"?"PendingSurface":"SavedSurface"];public string StatusLabel=>Status;public MediaItem(string n,string p,string f,long s){Name=n;Path=p;Format=f;SizeBytes=s;}public event PropertyChangedEventHandler? PropertyChanged;}
    public enum PendingType{AddMedia,AddFolder,Rename,Delete}
    public sealed class PendingChange : INotifyPropertyChanged
    {public PendingType Type{get;set;}public string ParentPath{get;set;}="";public string DisplayName{get;set;}="";public string? NewName{get;set;}public string? SourceUrl{get;set;}public string Format{get;set;}="MP3";public int Bitrate{get;set;}=128;public string Status{get;set;}="Waiting";double progress;public double Progress{get=>progress;set{progress=value;OnChanged();}}public bool IsPlaylist{get;set;}public event PropertyChangedEventHandler? PropertyChanged;public void OnChanged()=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(null));}
    public sealed class StoredState{public string? Language{get;set;}public string? Theme{get;set;}public string? DefaultFormat{get;set;}public int DefaultBitrate{get;set;}=128;public List<string>? RootFolders{get;set;}public List<PendingChange>? Pending{get;set;}}
}
public static class StringExtensions{public static T GetValueOrDefault<T>(this Dictionary<string,T> d,string k,T fallback)=>d.TryGetValue(k,out var v)?v:fallback;}