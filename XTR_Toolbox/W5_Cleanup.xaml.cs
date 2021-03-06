﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using JetBrains.Annotations;

namespace XTR_Toolbox
{
    public partial class Window5
    {
        private static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        private static readonly string SteamLibraryDir = GetSteamLibraryDir();
        
        private static bool _befDate;
        private static string _date;

        private readonly List<CheckBoxModel> _dirList = new List<CheckBoxModel>();
        private readonly OperationModel _operationBind = new OperationModel();
        private ObservableCollection<CleanModel> _cleanList;
        private SortAdorner _listViewSortAdorner;
        private GridViewColumnHeader _listViewSortCol;
        private bool _tipShown;

        public Window5()
        {
            InitializeComponent();
        }

        private static float AddModelToTemp(string baseDir, ICollection<CleanModel> tempList, string filePath)
        {
            string cleanedPath = filePath.Replace(baseDir, "").TrimStart('\\');
            FileInfo fileInfo = new FileInfo(filePath);
            float fileSize = fileInfo.Length / 1024 + 1;
            tempList.Add(new CleanModel
            {
                Path = cleanedPath,
                Size = fileSize + " KB",
                Date = fileInfo.LastAccessTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Extension = Path.GetExtension(cleanedPath).ToLower(),
                Group = baseDir
            });
            return fileSize;
        }

        private static void DeleteOp(IEnumerable input, ICollection<CleanModel> output, IProgress<int> progress)
        {
            int co = 0;
            foreach (CleanModel item in input)
            {
                string combinedPath = Path.Combine(item.Group, item.Path);
                progress.Report(++co);
                try
                {
                    File.Delete(combinedPath);
                    output.Add(item);
                    string directoryName = Path.GetDirectoryName(combinedPath);
                    if (directoryName != null)
                        Directory.Delete(directoryName);
                }
                catch
                {
                    //ignored
                }
            }
        }

        private static void DirectorySearch(IReadOnlyCollection<string> dirs, string multiExt,
            ICollection<CleanModel> tempList, int depth, bool isSteam, ref float maxSize)
        {
            if (dirs.Any())
            {
                List<string> typeToScan = new List<string>();
                if (multiExt != "")
                {
                    string[] split = multiExt.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string type in split)
                    {
                        string trimmed = type.Trim();
                        if (trimmed.StartsWith("*"))
                            typeToScan.Add(trimmed);
                        if (trimmed.StartsWith("."))
                            typeToScan.Add("*" + trimmed);
                    }
                }
                else
                {
                    typeToScan.Add("*");
                }

                maxSize += dirs.Sum(baseDir =>
                    GetDirectoryFilesLoop(baseDir, typeToScan, depth)
                        .Sum(fileFull => AddModelToTemp(baseDir, tempList, fileFull) / 1024));
            }

            if (isSteam)
                SteamLibrarySearch(SteamLibraryDir, tempList, ref maxSize);
        }

        private static IEnumerable<string> GetDirectoryFilesLoop(string root, List<string> types, int depth)
        {
            List<string> foundFiles = new List<string>();

            foreach (string ext in types)
                try
                {
                    if (_date.Length == 0)
                    {
                        foundFiles.AddRange(Directory.EnumerateFiles(root, ext));
                    }
                    else
                    {
                        List<string> dateList = new List<string>();
                        foreach (string item in Directory.EnumerateFiles(root, ext))
                        {
                            FileInfo ii = new FileInfo(item);
                            if (_befDate)
                            {
                                if (ii.LastAccessTime < DateTime.Parse(_date))
                                    dateList.Add(item);
                            }
                            else if (ii.LastAccessTime >= DateTime.Parse(_date))
                            {
                                dateList.Add(item);
                            }
                        }

                        foundFiles.AddRange(dateList);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                }

            if (depth <= 0) return foundFiles;
            try
            {
                foreach (string subDir in Directory.EnumerateDirectories(root))
                    foundFiles.AddRange(GetDirectoryFilesLoop(subDir, types, depth - 1));
            }
            catch (UnauthorizedAccessException)
            {
            }

            return foundFiles;
        }

        private static string GetSteamLibraryDir()
        {
            string programFilesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                @"Steam\SteamApps\common");
            const string altPath = @"D:\SteamLibrary\SteamApps\common";
            return Directory.Exists(altPath) ? altPath : programFilesPath;
        }

        private static void SteamLibrarySearch(string root, ICollection<CleanModel> tempList, ref float totalSize)
        {
            string[] patternName = {"Redist", "directx", "setup", "depends"};
            try
            {
                foreach (string gameDir in Directory.EnumerateDirectories(root))
                foreach (string pattDir in Directory.EnumerateDirectories(gameDir))
                {
                    if (!patternName.Any(pattDir.Contains)) continue;
                    try
                    {
                        totalSize += Directory.EnumerateFiles(pattDir, "*", SearchOption.AllDirectories)
                            .Sum(filePath => AddModelToTemp(root, tempList, filePath) / 1024);
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private void CheckBoxAll_Checked(object sender, RoutedEventArgs e)
        {
            foreach (CheckBoxModel t in _dirList)
                if (t.Enabled)
                    t.Checked = true;
        }

        private void CheckBoxAll_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (CheckBoxModel t in _dirList)
                t.Checked = false;
        }

        private void DeleteCmd_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
            e.CanExecute = LvCleaner.SelectedItems.Count > 0;

        private async void DeleteCmd_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (_operationBind.DeleteIndicator) return;
            _operationBind.DeleteIndicator = true;
            IList input = LvCleaner.SelectedItems;
            List<CleanModel> output = new List<CleanModel>();
            Progress<int> progress = new Progress<int>(s => _operationBind.DeleteProgress = s);
            await Task.Factory.StartNew(() => DeleteOp(input, output, progress));
            foreach (CleanModel item in output)
                _cleanList.Remove(item);
            _operationBind.DeleteIndicator = false;
        }

        private void DirSetup()
        {
            Dictionary<string, string> dirPreset = new Dictionary<string, string>
            {
                {"Temporary Directory", Path.GetTempPath()},
                {"Win Temporary Directory", Path.Combine(WinDir, @"Temp")},
                {"Windows Installer Cache", Path.Combine(WinDir, @"Installer\$PatchCache$\Managed")},
                {"Windows Update Cache", Path.Combine(WinDir, @"SoftwareDistribution\Download")},
                {"Windows Logs Directory", Path.Combine(WinDir, @"Logs")},
                {"Prefetch Cache", Path.Combine(WinDir, @"Prefetch")},
                {
                    "Crash Dump Directory",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        @"CrashDumps")
                },
                {"Steam Redist Packages", SteamLibraryDir}
            };
            foreach (KeyValuePair<string, string> oneDir in dirPreset)
                _dirList.Add(new CheckBoxModel
                {
                    Text = oneDir.Key,
                    Path = oneDir.Value,
                    Enabled = Directory.Exists(oneDir.Value)
                });
        }

        private void LbSidebar_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.RemovedItems.Count != 0 || e.AddedItems.Count == 0) return;
            CheckBoxModel t = (CheckBoxModel) e.AddedItems[0];
            if (t.Enabled)
                t.Checked = !t.Checked;
            LbDir.SelectedIndex = -1;
        }

        private void LvUsersColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            GridViewColumnHeader column = sender as GridViewColumnHeader;
            string sortBy = column?.Tag.ToString();
            if (_listViewSortCol != null)
            {
                AdornerLayer.GetAdornerLayer(_listViewSortCol).Remove(_listViewSortAdorner);
                LvCleaner.Items.SortDescriptions.Clear();
            }

            ListSortDirection newDir = ListSortDirection.Ascending;
            if (Equals(_listViewSortCol, column) && Equals(_listViewSortAdorner.Direction, newDir))
                newDir = ListSortDirection.Descending;

            _listViewSortCol = column;
            _listViewSortAdorner = new SortAdorner(_listViewSortCol, newDir);
            if (_listViewSortCol != null)
                AdornerLayer.GetAdornerLayer(_listViewSortCol).Add(_listViewSortAdorner);
            if (sortBy != null)
                LvCleaner.Items.SortDescriptions.Add(new SortDescription(sortBy, newDir));
        }

        private void OnCloseExecuted(object sender, ExecutedRoutedEventArgs e) => Close();

        private void ScanCmd_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
            e.CanExecute = LbDir.Items.Cast<CheckBoxModel>().Any(item => item.Checked);

        private async void ScanCmd_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            BtnAnalyze.Focus(); // NEED TO FOCUS BECAUSE OF DATE PICKER
            if (_operationBind.ScanIndicator) return;
            _operationBind.ScanIndicator = true;
            Stopwatch sw = Stopwatch.StartNew();
            bool isSteam = false;
            List<string> selectedDirs = new List<string>();
            foreach (CheckBoxModel item in _dirList)
            {
                if (!item.Checked) continue;
                if (item.Path.Equals(SteamLibraryDir))
                {
                    isSteam = true;
                    continue;
                }

                if (Directory.Exists(item.Path))
                    selectedDirs.Add(item.Path);
            }

            float maxSize = 0;
            string multiExt = TBoxTypes.Text.Trim();
            int depth = Convert.ToInt32(CbBoxLevel.SelectionBoxItem);
            _date = DateFilter.Text;
            _befDate = TglDate.IsChecked != null && (bool) !TglDate.IsChecked;
            List<CleanModel> tList = new List<CleanModel>();
            await Task.Factory.StartNew(() =>
                DirectorySearch(selectedDirs, multiExt, tList, depth, isSteam, ref maxSize));

            _cleanList = new ObservableCollection<CleanModel>(tList);
            LvCleaner.ItemsSource = _cleanList;
            if (CheckBoxGroup.IsChecked != null && (bool) CheckBoxGroup.IsChecked)
            {
                CollectionView view = (CollectionView) CollectionViewSource.GetDefaultView(LvCleaner.ItemsSource);
                view.GroupDescriptions.Add(new PropertyGroupDescription("Group"));
            }

            sw.Stop();

            float mSec = sw.ElapsedMilliseconds;
            _operationBind.ScanTime = mSec < 1000
                ? mSec + " ms"
                : (mSec / 1000).ToString("0.00") + " sec";
            _operationBind.MaxSize = Convert.ToInt32(maxSize);

            if (!_tipShown)
            {
                Shared.SnackBarTip(MainSnackbar);
                _tipShown = true;
            }

            _operationBind.ScanIndicator = false;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            DirSetup();
            LbDir.ItemsSource = _dirList;
            CbBoxLevel.ItemsSource = new[] {0, 1, 2, 3, 4, 5, 20};
            CbBoxLevel.SelectedIndex = 5;
            StackPanelBtns.DataContext = TbHeader.DataContext = _operationBind;
        }

        private class CheckBoxModel : INotifyPropertyChanged
        {
            private bool _checked;

            public event PropertyChangedEventHandler PropertyChanged;

            public bool Checked
            {
                get => _checked;
                set
                {
                    if (_checked == value) return;
                    _checked = value;
                    NotifyPropertyChanged(nameof(Checked));
                }
            }

            public bool Enabled { [UsedImplicitly] get; set; }
            public string Path { [UsedImplicitly] get; set; }

            public string Text { [UsedImplicitly] get; set; }

            private void NotifyPropertyChanged(string propName) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }

        private class CleanModel
        {
            public string Date { [UsedImplicitly] get; set; }
            public string Extension { [UsedImplicitly] get; set; }
            public string Group { [UsedImplicitly] get; set; }
            public string Path { [UsedImplicitly] get; set; }
            public string Size { [UsedImplicitly] get; set; }
        }

        private class OperationModel : INotifyPropertyChanged
        {
            private bool _deleteIndicator;
            private int _deleteprogress;
            private int _maxSize;
            private bool _scanIndicator;
            private string _scanTime;

            public event PropertyChangedEventHandler PropertyChanged;

            public bool DeleteIndicator
            {
                [UsedImplicitly] get => _deleteIndicator;
                set
                {
                    if (_deleteIndicator == value) return;
                    _deleteIndicator = value;
                    NotifyPropertyChanged(nameof(DeleteIndicator));
                }
            }

            public int DeleteProgress
            {
                [UsedImplicitly] get => _deleteprogress;
                set
                {
                    if (_deleteprogress == value) return;
                    _deleteprogress = value;
                    NotifyPropertyChanged(nameof(DeleteProgress));
                }
            }

            public int MaxSize
            {
                [UsedImplicitly] get => _maxSize;
                set
                {
                    if (_maxSize == value) return;
                    _maxSize = value;
                    NotifyPropertyChanged(nameof(MaxSize));
                }
            }

            public bool ScanIndicator
            {
                [UsedImplicitly] get => _scanIndicator;
                set
                {
                    if (_scanIndicator == value) return;
                    _scanIndicator = value;
                    NotifyPropertyChanged(nameof(ScanIndicator));
                }
            }

            public string ScanTime
            {
                [UsedImplicitly] get => _scanTime;

                set
                {
                    if (_scanTime == value) return;
                    _scanTime = value;
                    NotifyPropertyChanged(nameof(ScanTime));
                }
            }

            private void NotifyPropertyChanged(string propName) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }

    public static class CleanCmd
    {
        public static readonly RoutedCommand Delete = new RoutedCommand("Delete", typeof(CleanCmd),
            new InputGestureCollection {new KeyGesture(Key.Delete, ModifierKeys.Control)});

        public static readonly RoutedCommand Scan = new RoutedCommand("Scan", typeof(CleanCmd),
            new InputGestureCollection {new KeyGesture(Key.S, ModifierKeys.Control)});
    }
}