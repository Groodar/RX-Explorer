﻿using HtmlAgilityPack;
using Microsoft.Toolkit.Uwp.UI.Animations;
using Microsoft.UI.Xaml.Controls;
using RX_Explorer.Class;
using RX_Explorer.Dialog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.DragDrop;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Search;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using RefreshContainer = Microsoft.UI.Xaml.Controls.RefreshContainer;
using TreeView = Microsoft.UI.Xaml.Controls.TreeView;
using TreeViewCollapsedEventArgs = Microsoft.UI.Xaml.Controls.TreeViewCollapsedEventArgs;
using TreeViewExpandingEventArgs = Microsoft.UI.Xaml.Controls.TreeViewExpandingEventArgs;
using TreeViewItemInvokedEventArgs = Microsoft.UI.Xaml.Controls.TreeViewItemInvokedEventArgs;
using TreeViewNode = Microsoft.UI.Xaml.Controls.TreeViewNode;

namespace RX_Explorer
{
    public sealed partial class FileControl : Page, IDisposable
    {
        private volatile TreeViewNode currentnode;

        public TreeViewNode CurrentNode
        {
            get
            {
                return currentnode;
            }
            set
            {
                if (value != null && value.Content is TreeViewNodeContent Content)
                {
                    AreaWatcher.StartWatchDirectory(Content.Path, SettingControl.IsDisplayHiddenItem);
                    UpdateAddressButton(Content.Path);

                    CurrentPath = Content.Path;

                    TaskBarController.SetText(Content.DisplayName);

                    FolderTree.SelectNode(value);

                    Presenter.ItemPresenter.Focus(FocusState.Programmatic);

                    string PlaceText;
                    if (Content.DisplayName.Length > 22)
                    {
                        PlaceText = Content.DisplayName.Substring(0, 22) + "...";
                    }
                    else
                    {
                        PlaceText = Content.DisplayName;
                    }

                    GlobeSearch.PlaceholderText = $"{Globalization.GetString("SearchBox_PlaceholderText")} {PlaceText}";
                    GoParentFolder.IsEnabled = !FolderTree.RootNodes.Contains(value);
                    GoBackRecord.IsEnabled = RecordIndex > 0;
                    GoForwardRecord.IsEnabled = RecordIndex < GoAndBackRecord.Count - 1;

                    if (TabItem != null)
                    {
                        TabItem.Header = Content.DisplayName;
                    }
                }

                currentnode = value;
            }
        }

        private int TextChangeLockResource;

        private int AddressButtonLockResource;

        private int NavigateLockResource;

        private int DropLockResource;

        private string AddressBoxTextBackup;

        private volatile StorageFolder currentFolder;

        private SemaphoreSlim EnterLock;

        private StorageAreaWatcher AreaWatcher;

        private string CurrentPath { get; set; }

        public StorageFolder CurrentFolder
        {
            get
            {
                if (currentFolder != null)
                {
                    if (currentFolder.Path != CurrentPath)
                    {
                        return currentFolder = (CurrentNode?.Content as TreeViewNodeContent)?.GetStorageFolderAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                    }
                    else
                    {
                        return currentFolder;
                    }
                }
                else
                {
                    return currentFolder = (CurrentNode?.Content as TreeViewNodeContent)?.GetStorageFolderAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                }
            }
            set
            {
                if (value != null)
                {
                    AreaWatcher.StartWatchDirectory(value.Path, SettingControl.IsDisplayHiddenItem);
                    UpdateAddressButton(value.Path);
                    Presenter.ItemPresenter.Focus(FocusState.Programmatic);

                    string PlaceText = value.DisplayName.Length > 15 ? $"{value.DisplayName.Substring(0, 15)}..." : value.DisplayName;

                    GlobeSearch.PlaceholderText = $"{Globalization.GetString("SearchBox_PlaceholderText")} {PlaceText}";
                    GoParentFolder.IsEnabled = SettingControl.IsDetachTreeViewAndPresenter ? value.Path != Path.GetPathRoot(value.Path) : value.Path != (FolderTree.RootNodes[0].Content as TreeViewNodeContent)?.Path;
                    GoBackRecord.IsEnabled = RecordIndex > 0;
                    GoForwardRecord.IsEnabled = RecordIndex < GoAndBackRecord.Count - 1;

                    if (TabItem != null)
                    {
                        TabItem.Header = value.DisplayName;
                    }
                }

                TaskBarController.SetText(value?.DisplayName);

                currentFolder = value;
                CurrentPath = value?.Path;
            }
        }

        private int RecordIndex
        {
            get
            {
                return recordIndex;
            }
            set
            {
                recordIndex = value;
            }
        }

        public bool IsSearchOrPathBoxFocused { get; set; }
        private List<string> GoAndBackRecord = new List<string>();
        private ObservableCollection<string> AddressButtonList = new ObservableCollection<string>();
        private ObservableCollection<string> AddressExtentionList = new ObservableCollection<string>();
        private volatile int recordIndex;
        private bool IsBackOrForwardAction;
        private TabViewItem TabItem;

        public event PropertyChangedEventHandler PropertyChanged;

        public FileControl()
        {
            InitializeComponent();

            try
            {
                ItemDisplayMode.Items.Add(Globalization.GetString("FileControl_ItemDisplayMode_Tiles"));
                ItemDisplayMode.Items.Add(Globalization.GetString("FileControl_ItemDisplayMode_Details"));
                ItemDisplayMode.Items.Add(Globalization.GetString("FileControl_ItemDisplayMode_List"));
                ItemDisplayMode.Items.Add(Globalization.GetString("FileControl_ItemDisplayMode_Large_Icon"));
                ItemDisplayMode.Items.Add(Globalization.GetString("FileControl_ItemDisplayMode_Medium_Icon"));
                ItemDisplayMode.Items.Add(Globalization.GetString("FileControl_ItemDisplayMode_Small_Icon"));

                if (ApplicationData.Current.LocalSettings.Values["FilePresenterDisplayMode"] is int Index)
                {
                    ItemDisplayMode.SelectedIndex = Index;
                }
                else
                {
                    ApplicationData.Current.LocalSettings.Values["FilePresenterDisplayMode"] = 1;
                    ItemDisplayMode.SelectedIndex = 1;
                }
            }
            catch (Exception ex)
            {
                ExceptionTracer.RequestBlueScreen(ex);
            }
        }

        private void Current_Resuming(object sender, object e)
        {
            AreaWatcher.StartWatchDirectory(AreaWatcher.CurrentLocation, SettingControl.IsDisplayHiddenItem);
        }

        private void Current_Suspending(object sender, Windows.ApplicationModel.SuspendingEventArgs e)
        {
            AreaWatcher.StopWatchDirectory();
        }

        /// <summary>
        /// 激活或关闭正在加载提示
        /// </summary>
        /// <param name="IsLoading">激活或关闭</param>
        /// <param name="Info">提示内容</param>
        public async Task LoadingActivation(bool IsLoading, string Info = null)
        {
            if (LoadingControl.IsLoading == IsLoading)
            {
                return;
            }

            if (IsLoading)
            {
                if (Presenter.HasFile.Visibility == Visibility.Visible)
                {
                    Presenter.HasFile.Visibility = Visibility.Collapsed;
                }

                ProBar.IsIndeterminate = true;
                ProBar.Value = 0;
                ProgressInfo.Text = Info + "...";

                MainPage.ThisPage.IsAnyTaskRunning = true;
            }
            else
            {
                await Task.Delay(500).ConfigureAwait(true);
                MainPage.ThisPage.IsAnyTaskRunning = false;
            }

            LoadingControl.IsLoading = IsLoading;
        }

        private async void UpdateAddressButton(string Path)
        {
            if (Interlocked.Exchange(ref AddressButtonLockResource, 1) == 0)
            {
                try
                {
                    if (string.IsNullOrEmpty(Path))
                    {
                        return;
                    }

                    if (CurrentFolder == null)
                    {
                        string RootPath = System.IO.Path.GetPathRoot(Path);

                        StorageFolder DriveRootFolder = await StorageFolder.GetFolderFromPathAsync(RootPath);
                        AddressButtonList.Add(DriveRootFolder.DisplayName);

                        PathAnalysis Analysis = new PathAnalysis(Path, RootPath);

                        while (Analysis.HasNextLevel)
                        {
                            AddressButtonList.Add(Analysis.NextRelativePath());
                        }
                    }
                    else
                    {
                        string OriginalString = string.Join("\\", AddressButtonList.Skip(1));
                        string ActualString = System.IO.Path.Combine(System.IO.Path.GetPathRoot(CurrentFolder.Path), OriginalString);

                        List<string> IntersectList = new List<string>();
                        string[] FolderSplit = Path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                        string[] ActualSplit = ActualString.Split('\\', StringSplitOptions.RemoveEmptyEntries);

                        for (int i = 0; i < FolderSplit.Length && i < ActualSplit.Length; i++)
                        {
                            if (FolderSplit[i] == ActualSplit[i])
                            {
                                IntersectList.Add(FolderSplit[i]);
                            }
                            else
                            {
                                break;
                            }
                        }

                        if (IntersectList.Count == 0)
                        {
                            AddressButtonList.Clear();

                            string RootPath = System.IO.Path.GetPathRoot(Path);

                            StorageFolder DriveRootFolder = await StorageFolder.GetFolderFromPathAsync(RootPath);
                            AddressButtonList.Add(DriveRootFolder.DisplayName);

                            PathAnalysis Analysis = new PathAnalysis(Path, RootPath);

                            while (Analysis.HasNextLevel)
                            {
                                AddressButtonList.Add(Analysis.NextRelativePath());
                            }
                        }
                        else
                        {
                            for (int i = AddressButtonList.Count - 1; i >= IntersectList.Count; i--)
                            {
                                AddressButtonList.RemoveAt(i);
                            }

                            List<string> ExceptList = Path.Split('\\', StringSplitOptions.RemoveEmptyEntries).ToList();

                            ExceptList.RemoveRange(0, IntersectList.Count);

                            foreach (string SubPath in ExceptList)
                            {
                                AddressButtonList.Add(SubPath);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("UpdateAddressButton throw an exception, message: " + ex.Message);
                }
                finally
                {
                    AddressButtonContainer.UpdateLayout();

                    while(!AddressButtonContainer.IsLoaded)
                    {
                        await Task.Delay(500).ConfigureAwait(true);
                    }

                    ScrollViewer Viewer = AddressButtonContainer.FindChildOfType<ScrollViewer>();

                    if (Viewer.ActualWidth < Viewer.ExtentWidth)
                    {
                        Viewer.ChangeView(Viewer.ExtentWidth, null, null);
                    }

                    _ = Interlocked.Exchange(ref AddressButtonLockResource, 0);
                }
            }
        }

        public async Task OpenTargetFolder(StorageFolder Folder)
        {
            if (Folder == null)
            {
                throw new ArgumentNullException(nameof(Stream), "Argument could not be null");
            }

            Presenter.FileCollection.Clear();
            Presenter.HasFile.Visibility = Visibility.Collapsed;

            FolderTree.RootNodes.Clear();

            if (SettingControl.IsDetachTreeViewAndPresenter)
            {
                await DisplayItemsInFolder(Folder, true).ConfigureAwait(false);
            }
            else
            {
                StorageFolder RootFolder = await StorageFolder.GetFolderFromPathAsync(Path.GetPathRoot(Folder.Path));

                bool HasItem = (await RootFolder.GetFoldersAsync(CommonFolderQuery.DefaultQuery, 0, 1)).Count > 0;

                TreeViewNode RootNode = new TreeViewNode
                {
                    Content = new TreeViewNodeContent(RootFolder),
                    HasUnrealizedChildren = HasItem,
                    IsExpanded = HasItem
                };
                FolderTree.RootNodes.Add(RootNode);

                if (HasItem)
                {
                    await FillTreeNode(RootNode).ConfigureAwait(true);

                    TreeViewNode TargetNode = await RootNode.GetChildNodeAsync(new PathAnalysis(Folder.Path, string.Empty)).ConfigureAwait(true);

                    if (TargetNode == null)
                    {
                        QueueContentDialog dialog = new QueueContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                            Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                        };
                        _ = await dialog.ShowAsync().ConfigureAwait(true);
                    }
                    else
                    {
                        await DisplayItemsInFolder(TargetNode, true).ConfigureAwait(false);
                    }
                }
            }
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            if (e.NavigationMode == NavigationMode.New && e?.Parameter is Tuple<TabViewItem, StorageFolder> Parameters)
            {
                Application.Current.Suspending += Current_Suspending;
                Application.Current.Resuming += Current_Resuming;
                Frame.Navigated += Frame_Navigated;

                if (Parameters.Item1 != null)
                {
                    TabItem = Parameters.Item1;
                }

                AreaWatcher = new StorageAreaWatcher(Presenter.FileCollection, FolderTree);
                EnterLock = new SemaphoreSlim(1, 1);

                if (!CommonAccessCollection.FrameFileControlDic.ContainsKey(Frame))
                {
                    CommonAccessCollection.FrameFileControlDic.Add(Frame, this);
                }

                await Initialize(Parameters.Item2).ConfigureAwait(false);
            }
        }

        private void Frame_Navigated(object sender, NavigationEventArgs e)
        {
            if (e.Content is PhotoViewer)
            {
                TabItem.Header = Globalization.GetString("BuildIn_PhotoViewer_Description");
            }
            else if (e.Content is PdfReader)
            {
                TabItem.Header = Globalization.GetString("BuildIn_PdfReader_Description");
            }
            else if (e.Content is MediaPlayer)
            {
                TabItem.Header = Globalization.GetString("BuildIn_MediaPlayer_Description");
            }
            else if (e.Content is TextViewer)
            {
                TabItem.Header = Globalization.GetString("BuildIn_TextViewer_Description");
            }
            else if (e.Content is CropperPage)
            {
                TabItem.Header = Globalization.GetString("BuildIn_CropperPage_Description");
            }
            else if (e.Content is SearchPage)
            {
                TabItem.Header = Globalization.GetString("BuildIn_SearchPage_Description");
            }
            else
            {
                TabItem.Header = CurrentFolder.DisplayName;
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (e.NavigationMode == NavigationMode.Back)
            {
                Dispose();
            }
        }

        /// <summary>
        /// 执行文件目录的初始化
        /// </summary>
        public async Task Initialize(StorageFolder InitFolder)
        {
            if (InitFolder != null)
            {
                FolderTree.RootNodes.Clear();
                TreeViewNode RootNode = new TreeViewNode
                {
                    Content = new TreeViewNodeContent(InitFolder),
                    IsExpanded = false,
                    HasUnrealizedChildren = (await InitFolder.GetFoldersAsync(CommonFolderQuery.DefaultQuery, 0, 1)).Count > 0
                };

                FolderTree.RootNodes.Add(RootNode);

                if (SettingControl.IsDetachTreeViewAndPresenter)
                {
                    await DisplayItemsInFolder(InitFolder).ConfigureAwait(false);
                }
                else
                {
                    await DisplayItemsInFolder(RootNode).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// 向特定TreeViewNode节点下添加子节点
        /// </summary>
        /// <param name="Node">节点</param>
        /// <returns></returns>
        public async Task FillTreeNode(TreeViewNode Node)
        {
            if (Node == null)
            {
                throw new ArgumentNullException(nameof(Node), "Parameter could not be null");
            }

            if (Node.Content is TreeViewNodeContent Content)
            {
                try
                {
                    List<string> StorageItemPath = WIN_Native_API.GetStorageItemsPath(Content.Path, SettingControl.IsDisplayHiddenItem, ItemFilters.Folder);

                    for (int i = 0; i < StorageItemPath.Count && Node.IsExpanded && Node.CanTraceToRootNode(FolderTree.RootNodes.FirstOrDefault()); i++)
                    {
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                        {
                            TreeViewNode NewNode = new TreeViewNode
                            {
                                Content = new TreeViewNodeContent(StorageItemPath[i]),
                                HasUnrealizedChildren = WIN_Native_API.CheckContainsAnyItem(StorageItemPath[i], ItemFilters.Folder)
                            };

                            Node.Children.Add(NewNode);
                        });
                    }
                }
                catch (Exception ex)
                {
                    ExceptionTracer.RequestBlueScreen(ex);
                }
                finally
                {
                    if (!Node.IsExpanded)
                    {
                        Node.Children.Clear();
                    }
                }
            }
        }

        private async void FolderTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
        {
            await FillTreeNode(args.Node).ConfigureAwait(false);
        }

        private async void FolderTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            if (args.InvokedItem is TreeViewNode Node)
            {
                if (WIN_Native_API.CheckIfHidden((Node.Content as TreeViewNodeContent).Path))
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_ItemHidden_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };

                    _ = await Dialog.ShowAsync().ConfigureAwait(false);
                }
                else
                {
                    await DisplayItemsInFolder(Node).ConfigureAwait(false);
                }
            }
        }

        public async Task DisplayItemsInFolder(TreeViewNode Node, bool ForceRefresh = false)
        {
            await EnterLock.WaitAsync().ConfigureAwait(true);

            if (Node == null)
            {
                throw new ArgumentNullException(nameof(Node), "Parameter could not be null");
            }

            try
            {
                if (Node.Content is TreeViewNodeContent Content)
                {
                    if (!ForceRefresh)
                    {
                        if (Content.Path == CurrentFolder?.Path)
                        {
                            return;
                        }
                    }

                    if (IsBackOrForwardAction)
                    {
                        IsBackOrForwardAction = false;
                    }
                    else if (!ForceRefresh)
                    {
                        if (RecordIndex != GoAndBackRecord.Count - 1 && GoAndBackRecord.Count != 0)
                        {
                            GoAndBackRecord.RemoveRange(RecordIndex + 1, GoAndBackRecord.Count - RecordIndex - 1);
                        }

                        GoAndBackRecord.Add(Content.Path);

                        RecordIndex = GoAndBackRecord.Count - 1;
                    }

                    CurrentNode = Node;

                    Presenter.FileCollection.Clear();

                    if (await Content.GetStorageFolderAsync().ConfigureAwait(true) is StorageFolder Folder)
                    {
                        List<FileSystemStorageItemBase> ItemList = SortCollectionGenerator.Current.GetSortedCollection(WIN_Native_API.GetStorageItems(Content.Path, SettingControl.IsDisplayHiddenItem, ItemFilters.File | ItemFilters.Folder));

                        Presenter.HasFile.Visibility = ItemList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                        Presenter.StatusTips.Text = Globalization.GetString("FilePresenterBottomStatusTip_TotalItem").Replace("{ItemNum}", ItemList.Count.ToString());

                        foreach (FileSystemStorageItemBase Item in ItemList)
                        {
                            Presenter.FileCollection.Add(Item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                QueueContentDialog Dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = $"{Globalization.GetString("QueueDialog_AccessFolderFailure_Content")} {ex.Message}",
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };

                _ = await Dialog.ShowAsync().ConfigureAwait(true);
            }
            finally
            {
                EnterLock.Release();
            }
        }

        public async Task DisplayItemsInFolder(StorageFolder Folder, bool ForceRefresh = false)
        {
            await EnterLock.WaitAsync().ConfigureAwait(true);

            if (Folder == null)
            {
                throw new ArgumentNullException(nameof(Folder), "Parameter could not be null");
            }

            try
            {
                if (!ForceRefresh)
                {
                    if (Folder.Path == CurrentFolder?.Path)
                    {
                        return;
                    }
                }

                if (IsBackOrForwardAction)
                {
                    IsBackOrForwardAction = false;
                }
                else if (!ForceRefresh)
                {
                    if (RecordIndex != GoAndBackRecord.Count - 1 && GoAndBackRecord.Count != 0)
                    {
                        GoAndBackRecord.RemoveRange(RecordIndex + 1, GoAndBackRecord.Count - RecordIndex - 1);
                    }

                    GoAndBackRecord.Add(Folder.Path);

                    RecordIndex = GoAndBackRecord.Count - 1;
                }

                CurrentFolder = Folder;

                Presenter.FileCollection.Clear();

                List<FileSystemStorageItemBase> ItemList = SortCollectionGenerator.Current.GetSortedCollection(WIN_Native_API.GetStorageItems(Folder, SettingControl.IsDisplayHiddenItem, ItemFilters.File | ItemFilters.Folder));

                Presenter.HasFile.Visibility = ItemList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                Presenter.StatusTips.Text = Globalization.GetString("FilePresenterBottomStatusTip_TotalItem").Replace("{ItemNum}", ItemList.Count.ToString());

                for (int i = 0; i < ItemList.Count; i++)
                {
                    Presenter.FileCollection.Add(ItemList[i]);
                }
            }
            catch (Exception ex)
            {
                QueueContentDialog Dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = $"{Globalization.GetString("QueueDialog_AccessFolderFailure_Content")} {ex.Message}",
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };

                _ = await Dialog.ShowAsync().ConfigureAwait(true);
            }
            finally
            {
                EnterLock.Release();
            }
        }

        private async void FolderDelete_Click(object sender, RoutedEventArgs e)
        {
            if (!WIN_Native_API.CheckExist(CurrentFolder.Path))
            {
                QueueContentDialog dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };

                _ = await dialog.ShowAsync().ConfigureAwait(true);
                return;
            }

            if (Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down))
            {
                await LoadingActivation(true, Globalization.GetString("Progress_Tip_Deleting")).ConfigureAwait(true);

            Retry:
                try
                {
                    await FullTrustProcessController.Current.DeleteAsync(CurrentFolder, true).ConfigureAwait(true);

                    await DisplayItemsInFolder(CurrentNode.Parent).ConfigureAwait(true);

                    await CurrentNode.UpdateAllSubNodeAsync().ConfigureAwait(true);
                }
                catch (FileCaputureException)
                {
                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_Item_Captured_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };

                    _ = await dialog.ShowAsync().ConfigureAwait(true);
                }
                catch (FileNotFoundException)
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_DeleteItemError_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_RefreshButton")
                    };

                    _ = await Dialog.ShowAsync().ConfigureAwait(true);

                    await DisplayItemsInFolder(CurrentNode.Parent).ConfigureAwait(true);
                }
                catch (InvalidOperationException)
                {
                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_UnauthorizedDelete_Content"),
                        PrimaryButtonText = Globalization.GetString("Common_Dialog_GrantButton"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CancelButton")
                    };

                    if (await dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                    {
                        if (await FullTrustProcessController.Current.SwitchToAdminMode().ConfigureAwait(true))
                        {
                            goto Retry;
                        }
                        else
                        {
                            QueueContentDialog ErrorDialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_DenyElevation_Content"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                            };

                            _ = await ErrorDialog.ShowAsync().ConfigureAwait(true);
                        }
                    }
                }
                catch (Exception)
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_DeleteItemError_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };
                    _ = await Dialog.ShowAsync().ConfigureAwait(true);
                }

                await LoadingActivation(false).ConfigureAwait(true);
            }
            else
            {
                DeleteDialog QueueContenDialog = new DeleteDialog(Globalization.GetString("QueueDialog_DeleteFolder_Content"));

                if (await QueueContenDialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                {
                    await LoadingActivation(true, Globalization.GetString("Progress_Tip_Deleting")).ConfigureAwait(true);

                Retry:
                    try
                    {
                        await FullTrustProcessController.Current.DeleteAsync(CurrentFolder, QueueContenDialog.IsPermanentDelete).ConfigureAwait(true);

                        await DisplayItemsInFolder(CurrentNode.Parent).ConfigureAwait(true);

                        await CurrentNode.UpdateAllSubNodeAsync().ConfigureAwait(true);
                    }
                    catch (FileCaputureException)
                    {
                        QueueContentDialog dialog = new QueueContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                            Content = Globalization.GetString("QueueDialog_Item_Captured_Content"),
                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                        };

                        _ = await dialog.ShowAsync().ConfigureAwait(true);
                    }
                    catch (FileNotFoundException)
                    {
                        QueueContentDialog Dialog = new QueueContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                            Content = Globalization.GetString("QueueDialog_DeleteItemError_Content"),
                            CloseButtonText = Globalization.GetString("Common_Dialog_RefreshButton")
                        };

                        _ = await Dialog.ShowAsync().ConfigureAwait(true);

                        await DisplayItemsInFolder(CurrentNode.Parent).ConfigureAwait(true);
                    }
                    catch (InvalidOperationException)
                    {
                        QueueContentDialog dialog = new QueueContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                            Content = Globalization.GetString("QueueDialog_UnauthorizedDelete_Content"),
                            PrimaryButtonText = Globalization.GetString("Common_Dialog_GrantButton"),
                            CloseButtonText = Globalization.GetString("Common_Dialog_CancelButton")
                        };

                        if (await dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                        {
                            if (await FullTrustProcessController.Current.SwitchToAdminMode().ConfigureAwait(true))
                            {
                                goto Retry;
                            }
                            else
                            {
                                QueueContentDialog ErrorDialog = new QueueContentDialog
                                {
                                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                    Content = Globalization.GetString("QueueDialog_DenyElevation_Content"),
                                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                };

                                _ = await ErrorDialog.ShowAsync().ConfigureAwait(true);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        QueueContentDialog Dialog = new QueueContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                            Content = Globalization.GetString("QueueDialog_DeleteItemError_Content"),
                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                        };
                        _ = await Dialog.ShowAsync().ConfigureAwait(true);
                    }

                    await LoadingActivation(false).ConfigureAwait(true);
                }
            }
        }

        private void FolderTree_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
        {
            args.Node.Children.Clear();
        }

        private async void FolderTree_RightTapped(object sender, Windows.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (e.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Mouse)
            {
                if ((e.OriginalSource as FrameworkElement)?.DataContext is TreeViewNode Node)
                {
                    if (WIN_Native_API.CheckIfHidden((Node.Content as TreeViewNodeContent).Path))
                    {
                        FolderTree.ContextFlyout = null;
                    }
                    else
                    {
                        FolderTree.ContextFlyout = RightTabFlyout;

                        await DisplayItemsInFolder(Node).ConfigureAwait(true);

                        if (FolderTree.RootNodes.Contains(CurrentNode))
                        {
                            FolderDelete.IsEnabled = false;
                            FolderRename.IsEnabled = false;
                            FolderAdd.IsEnabled = false;
                        }
                        else
                        {
                            FolderDelete.IsEnabled = true;
                            FolderRename.IsEnabled = true;
                            FolderAdd.IsEnabled = true;
                        }
                    }
                }
                else
                {
                    FolderTree.ContextFlyout = null;
                }
            }
        }

        private async void FolderRename_Click(object sender, RoutedEventArgs e)
        {
            if (!WIN_Native_API.CheckExist(CurrentFolder.Path))
            {
                QueueContentDialog ErrorDialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };

                _ = await ErrorDialog.ShowAsync().ConfigureAwait(true);
                return;
            }

            RenameDialog dialog = new RenameDialog(CurrentFolder);

            if ((await dialog.ShowAsync().ConfigureAwait(true)) == ContentDialogResult.Primary)
            {
                try
                {
                    await CurrentFolder.RenameAsync(dialog.DesireName);

                    (CurrentNode.Content as TreeViewNodeContent).Update(CurrentFolder);

                    UpdateAddressButton(CurrentFolder.Path);
                }
                catch (UnauthorizedAccessException)
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_UnauthorizedRename_StartExplorer_Content"),
                        PrimaryButtonText = Globalization.GetString("Common_Dialog_NowButton"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_LaterButton")
                    };

                    if (await Dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                    {
                        _ = await Launcher.LaunchFolderAsync(CurrentFolder);
                    }
                }
                catch (FileLoadException)
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_FolderOccupied_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                    };

                    _ = await Dialog.ShowAsync().ConfigureAwait(true);
                }
                catch
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_RenameExist_Content"),
                        PrimaryButtonText = Globalization.GetString("Common_Dialog_ContinueButton"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CancelButton")
                    };

                    if (await Dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                    {
                        await CurrentFolder.RenameAsync(dialog.DesireName, NameCollisionOption.GenerateUniqueName);

                        (CurrentNode.Content as TreeViewNodeContent).Update(CurrentFolder);

                        UpdateAddressButton(CurrentFolder.Path);
                    }
                }
            }
        }

        private async void CreateFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!WIN_Native_API.CheckExist(CurrentFolder.Path))
            {
                QueueContentDialog dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };

                _ = await dialog.ShowAsync().ConfigureAwait(true);
                return;
            }

            try
            {
                _ = await CurrentFolder.CreateFolderAsync(Globalization.GetString("Create_NewFolder_Admin_Name"), CreationCollisionOption.GenerateUniqueName);
            }
            catch (UnauthorizedAccessException)
            {
                QueueContentDialog dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_UnauthorizedCreateFolder_Content"),
                    PrimaryButtonText = Globalization.GetString("Common_Dialog_NowButton"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_LaterButton")
                };

                if (await dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                {
                    _ = await Launcher.LaunchFolderAsync(CurrentFolder);
                }
            }
        }

        private async void FolderAttribute_Click(object sender, RoutedEventArgs e)
        {
            if (!WIN_Native_API.CheckExist(CurrentFolder.Path))
            {
                QueueContentDialog dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };
                _ = await dialog.ShowAsync().ConfigureAwait(true);

                return;
            }

            if (CurrentNode == FolderTree.RootNodes.FirstOrDefault())
            {
                if (CommonAccessCollection.HardDeviceList.FirstOrDefault((Device) => Device.Name == CurrentFolder.DisplayName) is HardDeviceInfo Info)
                {
                    DeviceInfoDialog dialog = new DeviceInfoDialog(Info);
                    _ = await dialog.ShowAsync().ConfigureAwait(true);
                }
                else
                {
                    PropertyDialog Dialog = new PropertyDialog(CurrentFolder);
                    _ = await Dialog.ShowAsync().ConfigureAwait(true);
                }
            }
            else
            {
                PropertyDialog Dialog = new PropertyDialog(CurrentFolder);
                _ = await Dialog.ShowAsync().ConfigureAwait(true);
            }
        }

        private async void FolderAdd_Click(object sender, RoutedEventArgs e)
        {
            if (!WIN_Native_API.CheckExist(CurrentFolder.Path))
            {
                QueueContentDialog dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };
                _ = await dialog.ShowAsync().ConfigureAwait(true);

                return;
            }

            if (CommonAccessCollection.LibraryFolderList.Any((Folder) => Folder.Folder.Path == CurrentFolder.Path))
            {
                QueueContentDialog dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_TipTitle"),
                    Content = Globalization.GetString("QueueDialog_RepeatAddToHomePage_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };
                _ = await dialog.ShowAsync().ConfigureAwait(true);
            }
            else
            {
                BitmapImage Thumbnail = await CurrentFolder.GetThumbnailBitmapAsync().ConfigureAwait(true);
                CommonAccessCollection.LibraryFolderList.Add(new LibraryFolder(CurrentFolder, Thumbnail, LibraryType.UserCustom));
                await SQLite.Current.SetLibraryPathAsync(CurrentFolder.Path, LibraryType.UserCustom).ConfigureAwait(false);
            }
        }

        private async void GlobeSearch_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(args.QueryText))
            {
                return;
            }

            FlyoutBase.ShowAttachedFlyout(sender);

            await SQLite.Current.SetSearchHistoryAsync(args.QueryText).ConfigureAwait(false);
        }

        private async void GlobeSearch_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (!string.IsNullOrWhiteSpace(sender.Text))
            {
                if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                {
                    sender.ItemsSource = await SQLite.Current.GetRelatedSearchHistoryAsync(sender.Text).ConfigureAwait(true);
                }
            }
        }

        private void SearchConfirm_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SearchFlyout.Hide();

                QueryOptions Options;
                if (ShallowRadio.IsChecked.GetValueOrDefault())
                {
                    Options = new QueryOptions(CommonFolderQuery.DefaultQuery)
                    {
                        FolderDepth = FolderDepth.Shallow,
                        IndexerOption = IndexerOption.UseIndexerWhenAvailable,
                        ApplicationSearchFilter = "System.FileName:*" + GlobeSearch.Text + "*"
                    };
                }
                else
                {
                    Options = new QueryOptions(CommonFolderQuery.DefaultQuery)
                    {
                        FolderDepth = FolderDepth.Deep,
                        IndexerOption = IndexerOption.UseIndexerWhenAvailable,
                        ApplicationSearchFilter = "System.FileName:*" + GlobeSearch.Text + "*"
                    };
                }

                Options.SetThumbnailPrefetch(ThumbnailMode.ListView, 100, ThumbnailOptions.ResizeThumbnail);
                Options.SetPropertyPrefetch(PropertyPrefetchOptions.BasicProperties, new string[] { "System.ItemTypeText", "System.ItemNameDisplayWithoutExtension", "System.FileName", "System.Size", "System.DateModified" });

                StorageItemQueryResult FileQuery = CurrentFolder.CreateItemQueryWithOptions(Options);

                if (AnimationController.Current.IsEnableAnimation)
                {
                    Frame.Navigate(typeof(SearchPage), new Tuple<FileControl, StorageItemQueryResult>(this, FileQuery), new DrillInNavigationTransitionInfo());
                }
                else
                {
                    Frame.Navigate(typeof(SearchPage), new Tuple<FileControl, StorageItemQueryResult>(this, FileQuery), new SuppressNavigationTransitionInfo());
                }
            }
            catch (Exception ex)
            {
                ExceptionTracer.RequestBlueScreen(ex);
            }
        }

        private void SearchCancel_Click(object sender, RoutedEventArgs e)
        {
            SearchFlyout.Hide();
        }

        private void SearchFlyout_Opened(object sender, object e)
        {
            _ = SearchConfirm.Focus(FocusState.Programmatic);
        }

        private async void GlobeSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            IsSearchOrPathBoxFocused = true;
            if (string.IsNullOrEmpty(GlobeSearch.Text))
            {
                GlobeSearch.ItemsSource = await SQLite.Current.GetRelatedSearchHistoryAsync(string.Empty).ConfigureAwait(true);
            }
        }

        private async void AddressBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            LoadingControl.Focus(FocusState.Programmatic);

            string QueryText = string.Empty;
            if (args.ChosenSuggestion == null)
            {
                if (string.IsNullOrEmpty(AddressBoxTextBackup))
                {
                    return;
                }
                else
                {
                    QueryText = AddressBoxTextBackup;
                }
            }
            else
            {
                QueryText = args.ChosenSuggestion.ToString();
            }

            if (QueryText == CurrentFolder.Path)
            {
                return;
            }

            if (string.Equals(QueryText, "Powershell", StringComparison.OrdinalIgnoreCase) || string.Equals(QueryText, "Powershell.exe", StringComparison.OrdinalIgnoreCase))
            {
                string ExcutePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe");

            Retry1:
                try
                {
                    await FullTrustProcessController.Current.RunAsync(ExcutePath, true, false, "-NoExit", "-Command", "Set-Location", CurrentFolder.Path).ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    QueueContentDialog UnauthorizeDialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_UnauthorizedExecute_Content"),
                        PrimaryButtonText = Globalization.GetString("Common_Dialog_GrantButton"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CancelButton")
                    };

                    if (await UnauthorizeDialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                    {
                        if (await FullTrustProcessController.Current.SwitchToAdminMode().ConfigureAwait(true))
                        {
                            goto Retry1;
                        }
                        else
                        {
                            QueueContentDialog ErrorDialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_DenyElevation_Content"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                            };

                            _ = await ErrorDialog.ShowAsync().ConfigureAwait(true);
                        }
                    }
                }

                return;
            }

            if (string.Equals(QueryText, "Cmd", StringComparison.OrdinalIgnoreCase) || string.Equals(QueryText, "Cmd.exe", StringComparison.OrdinalIgnoreCase))
            {
                string ExcutePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

            Retry2:
                try
                {
                    await FullTrustProcessController.Current.RunAsync(ExcutePath, true, false, "/k", "cd", "/d", CurrentFolder.Path).ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    QueueContentDialog UnauthorizeDialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_UnauthorizedExecute_Content"),
                        PrimaryButtonText = Globalization.GetString("Common_Dialog_GrantButton"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CancelButton")
                    };

                    if (await UnauthorizeDialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                    {
                        if (await FullTrustProcessController.Current.SwitchToAdminMode().ConfigureAwait(true))
                        {
                            goto Retry2;
                        }
                        else
                        {
                            QueueContentDialog ErrorDialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_DenyElevation_Content"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                            };

                            _ = await ErrorDialog.ShowAsync().ConfigureAwait(true);
                        }
                    }
                }

                return;
            }

            if (string.Equals(QueryText, "Wt", StringComparison.OrdinalIgnoreCase) || string.Equals(QueryText, "Wt.exe", StringComparison.OrdinalIgnoreCase))
            {
                switch (await Launcher.QueryUriSupportAsync(new Uri("ms-windows-store:"), LaunchQuerySupportType.Uri, "Microsoft.WindowsTerminal_8wekyb3d8bbwe"))
                {
                    case LaunchQuerySupportStatus.Available:
                    case LaunchQuerySupportStatus.NotSupported:
                        {
                        Retry:
                            try
                            {
                                await FullTrustProcessController.Current.RunAsync("wt.exe", false, false, "/d", CurrentFolder.Path).ConfigureAwait(false);
                            }
                            catch (InvalidOperationException)
                            {
                                QueueContentDialog UnauthorizeDialog = new QueueContentDialog
                                {
                                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                    Content = Globalization.GetString("QueueDialog_UnauthorizedExecute_Content"),
                                    PrimaryButtonText = Globalization.GetString("Common_Dialog_GrantButton"),
                                    CloseButtonText = Globalization.GetString("Common_Dialog_CancelButton")
                                };

                                if (await UnauthorizeDialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                                {
                                    if (await FullTrustProcessController.Current.SwitchToAdminMode().ConfigureAwait(true))
                                    {
                                        goto Retry;
                                    }
                                    else
                                    {
                                        QueueContentDialog ErrorDialog = new QueueContentDialog
                                        {
                                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                            Content = Globalization.GetString("QueueDialog_DenyElevation_Content"),
                                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                        };

                                        _ = await ErrorDialog.ShowAsync().ConfigureAwait(true);
                                    }
                                }
                            }

                            break;
                        }
                }

                return;
            }

            string ProtentialPath1 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), QueryText);
            string ProtentialPath2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), QueryText);
            string ProtentialPath3 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), QueryText);

            if (WIN_Native_API.CheckExist(ProtentialPath1))
            {
                if (WIN_Native_API.GetStorageItems(ProtentialPath1).FirstOrDefault() is FileSystemStorageItemBase Item)
                {
                    await Presenter.EnterSelectedItem(Item).ConfigureAwait(true);
                }
                return;
            }
            else if (WIN_Native_API.CheckExist(ProtentialPath2))
            {
                if (WIN_Native_API.GetStorageItems(ProtentialPath2).FirstOrDefault() is FileSystemStorageItemBase Item)
                {
                    await Presenter.EnterSelectedItem(Item).ConfigureAwait(true);
                }
                return;
            }
            else if (WIN_Native_API.CheckExist(ProtentialPath3))
            {
                if (WIN_Native_API.GetStorageItems(ProtentialPath3).FirstOrDefault() is FileSystemStorageItemBase Item)
                {
                    await Presenter.EnterSelectedItem(Item).ConfigureAwait(true);
                }
                return;
            }

            try
            {
                QueryText = await CommonEnvironmentVariables.ReplaceVariableAndGetActualPath(QueryText).ConfigureAwait(true);

                if (WIN_Native_API.CheckIfHidden(QueryText))
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_ItemHidden_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };

                    _ = await Dialog.ShowAsync().ConfigureAwait(false);

                    return;
                }

                if (!SettingControl.IsDetachTreeViewAndPresenter && !SettingControl.IsDisplayHiddenItem)
                {
                    PathAnalysis Analysis = new PathAnalysis(QueryText, string.Empty);
                    while (Analysis.HasNextLevel)
                    {
                        if (WIN_Native_API.CheckIfHidden(Analysis.NextFullPath()))
                        {
                            QueueContentDialog Dialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_NeedOpenHiddenSwitch_Content"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                            };

                            _ = await Dialog.ShowAsync().ConfigureAwait(false);
                            return;
                        }
                    }
                }

                if (Path.IsPathRooted(QueryText) && CommonAccessCollection.HardDeviceList.Any((Drive) => Drive.Folder.Path == Path.GetPathRoot(QueryText)))
                {
                    StorageFile File = await StorageFile.GetFileFromPathAsync(QueryText);
                    if (!await Launcher.LaunchFileAsync(File))
                    {
                        LauncherOptions options = new LauncherOptions
                        {
                            DisplayApplicationPicker = true
                        };
                        _ = await Launcher.LaunchFileAsync(File, options);
                    }
                }
                else
                {
                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} \r\"{QueryText}\"",
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                    };
                    _ = await dialog.ShowAsync().ConfigureAwait(true);
                }
            }
            catch (Exception)
            {
                try
                {
                    StorageFolder Folder = await StorageFolder.GetFolderFromPathAsync(QueryText);

                    if (SettingControl.IsDetachTreeViewAndPresenter)
                    {
                        await DisplayItemsInFolder(Folder).ConfigureAwait(true);

                        await SQLite.Current.SetPathHistoryAsync(Folder.Path).ConfigureAwait(true);
                    }
                    else
                    {
                        if (QueryText.StartsWith((FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path))
                        {
                            TreeViewNode TargetNode = await FolderTree.RootNodes[0].GetChildNodeAsync(new PathAnalysis(Folder.Path, (FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path)).ConfigureAwait(true);

                            if (TargetNode != null)
                            {
                                await DisplayItemsInFolder(TargetNode).ConfigureAwait(true);

                                await SQLite.Current.SetPathHistoryAsync(Folder.Path).ConfigureAwait(true);
                            }
                        }
                        else
                        {
                            await OpenTargetFolder(Folder).ConfigureAwait(true);

                            await SQLite.Current.SetPathHistoryAsync(Folder.Path).ConfigureAwait(true);
                        }
                    }
                }
                catch (Exception)
                {
                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} \r\"{QueryText}\"",
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                    };
                    _ = await dialog.ShowAsync().ConfigureAwait(true);
                }
            }
        }

        private void AddressBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            AddressBoxTextBackup = sender.Text;

            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                if (Path.IsPathRooted(sender.Text)
                    && Path.GetDirectoryName(sender.Text) is string DirectoryName
                    && CommonAccessCollection.HardDeviceList.Any((Drive) => Drive.Folder.Path == Path.GetPathRoot(sender.Text)))
                {
                    if (Interlocked.Exchange(ref TextChangeLockResource, 1) == 0)
                    {
                        try
                        {
                            if (args.CheckCurrent())
                            {
                                sender.ItemsSource = WIN_Native_API.GetStorageItems(DirectoryName, false, ItemFilters.Folder).Where((Item) => Item.Name.StartsWith(Path.GetFileName(sender.Text), StringComparison.OrdinalIgnoreCase)).Select((It) => It.Path);
                            }
                            else
                            {
                                sender.ItemsSource = null;
                            }
                        }
                        catch (Exception)
                        {
                            sender.ItemsSource = null;
                        }
                        finally
                        {
                            _ = Interlocked.Exchange(ref TextChangeLockResource, 0);
                        }
                    }
                }
            }
        }

        public async void GoParentFolder_Click(object sender, RoutedEventArgs e)
        {
            if (Interlocked.Exchange(ref NavigateLockResource, 1) == 0)
            {
                try
                {
                    if (GoParentFolder.IsEnabled)
                    {
                        string DirectoryPath = Path.GetDirectoryName(CurrentFolder.Path);

                        if (!string.IsNullOrWhiteSpace(DirectoryPath))
                        {
                            if (WIN_Native_API.CheckIfHidden(DirectoryPath))
                            {
                                QueueContentDialog Dialog = new QueueContentDialog
                                {
                                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                    Content = Globalization.GetString("QueueDialog_ItemHidden_Content"),
                                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                };

                                _ = await Dialog.ShowAsync().ConfigureAwait(false);

                                return;
                            }

                            if (SettingControl.IsDetachTreeViewAndPresenter)
                            {
                                if ((await CurrentFolder.GetParentAsync()) is StorageFolder ParentFolder)
                                {
                                    await DisplayItemsInFolder(ParentFolder).ConfigureAwait(false);
                                }
                            }
                            else
                            {
                                if ((await CurrentFolder.GetParentAsync()) is StorageFolder ParentFolder)
                                {
                                    TreeViewNode ParentNode = await FolderTree.RootNodes[0].GetChildNodeAsync(new PathAnalysis(ParentFolder.Path, (FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path)).ConfigureAwait(true);

                                    if (ParentFolder != null)
                                    {
                                        await DisplayItemsInFolder(ParentNode).ConfigureAwait(false);
                                    }
                                }
                            }
                        }
                        else
                        {
                            GoParentFolder.IsEnabled = false;
                        }
                    }
                }
                finally
                {
                    _ = Interlocked.Exchange(ref NavigateLockResource, 0);
                }
            }
        }

        public async void GoBackRecord_Click(object sender, RoutedEventArgs e)
        {
            if (Interlocked.Exchange(ref NavigateLockResource, 1) == 0)
            {
                string Path = string.Empty;

                try
                {
                    if (GoBackRecord.IsEnabled)
                    {
                        Path = GoAndBackRecord[--RecordIndex];

                        if (WIN_Native_API.CheckIfHidden(Path))
                        {
                            QueueContentDialog Dialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_ItemHidden_Content"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                            };

                            _ = await Dialog.ShowAsync().ConfigureAwait(false);

                            _ = Interlocked.Exchange(ref NavigateLockResource, 0);
                            return;
                        }

                        StorageFolder Folder = await StorageFolder.GetFolderFromPathAsync(Path);

                        IsBackOrForwardAction = true;

                        if (SettingControl.IsDetachTreeViewAndPresenter)
                        {
                            await DisplayItemsInFolder(Folder).ConfigureAwait(true);

                            await SQLite.Current.SetPathHistoryAsync(Folder.Path).ConfigureAwait(true);
                        }
                        else
                        {
                            if (Path.StartsWith((FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path))
                            {
                                TreeViewNode TargetNode = await FolderTree.RootNodes[0].GetChildNodeAsync(new PathAnalysis(Folder.Path, (FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path)).ConfigureAwait(true);

                                if (TargetNode == null)
                                {
                                    QueueContentDialog dialog = new QueueContentDialog
                                    {
                                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                        Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                    };
                                    _ = await dialog.ShowAsync().ConfigureAwait(true);
                                }
                                else
                                {
                                    await DisplayItemsInFolder(TargetNode).ConfigureAwait(true);

                                    await SQLite.Current.SetPathHistoryAsync(Folder.Path).ConfigureAwait(true);
                                }
                            }
                            else
                            {
                                await OpenTargetFolder(Folder).ConfigureAwait(true);

                                await SQLite.Current.SetPathHistoryAsync(Folder.Path).ConfigureAwait(true);
                            }
                        }

                    }
                }
                catch (Exception)
                {
                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} \r\"{Path}\"",
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                    };
                    _ = await dialog.ShowAsync().ConfigureAwait(true);

                    RecordIndex++;
                }
                finally
                {
                    _ = Interlocked.Exchange(ref NavigateLockResource, 0);
                }
            }
        }

        public async void GoForwardRecord_Click(object sender, RoutedEventArgs e)
        {
            if (Interlocked.Exchange(ref NavigateLockResource, 1) == 0)
            {
                string Path = string.Empty;

                try
                {
                    if (GoForwardRecord.IsEnabled)
                    {
                        Path = GoAndBackRecord[++RecordIndex];

                        if (WIN_Native_API.CheckIfHidden(Path))
                        {
                            QueueContentDialog Dialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_ItemHidden_Content"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                            };

                            _ = await Dialog.ShowAsync().ConfigureAwait(false);

                            _ = Interlocked.Exchange(ref NavigateLockResource, 0);

                            return;
                        }


                        StorageFolder Folder = await StorageFolder.GetFolderFromPathAsync(Path);

                        IsBackOrForwardAction = true;

                        if (SettingControl.IsDetachTreeViewAndPresenter)
                        {
                            await DisplayItemsInFolder(Folder).ConfigureAwait(true);

                            await SQLite.Current.SetPathHistoryAsync(Folder.Path).ConfigureAwait(true);
                        }
                        else
                        {
                            if (Path.StartsWith((FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path))
                            {
                                TreeViewNode TargetNode = await FolderTree.RootNodes[0].GetChildNodeAsync(new PathAnalysis(Folder.Path, (FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path)).ConfigureAwait(true);

                                if (TargetNode == null)
                                {
                                    QueueContentDialog dialog = new QueueContentDialog
                                    {
                                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                        Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                    };
                                    _ = await dialog.ShowAsync().ConfigureAwait(true);
                                }
                                else
                                {
                                    await DisplayItemsInFolder(TargetNode).ConfigureAwait(true);

                                    await SQLite.Current.SetPathHistoryAsync(Folder.Path).ConfigureAwait(true);
                                }
                            }
                            else
                            {
                                await OpenTargetFolder(Folder).ConfigureAwait(true);

                                await SQLite.Current.SetPathHistoryAsync(Folder.Path).ConfigureAwait(true);
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} \r\"{Path}\"",
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                    };
                    _ = await dialog.ShowAsync().ConfigureAwait(true);

                    RecordIndex--;
                }
                finally
                {
                    _ = Interlocked.Exchange(ref NavigateLockResource, 0);
                }
            }
        }

        private async void AddressBox_GotFocus(object sender, RoutedEventArgs e)
        {
            IsSearchOrPathBoxFocused = true;

            if (string.IsNullOrEmpty(AddressBox.Text))
            {
                AddressBox.Text = CurrentFolder.Path;
            }

            AddressButtonContainer.Visibility = Visibility.Collapsed;

            AddressBox.ItemsSource = await SQLite.Current.GetRelatedPathHistoryAsync().ConfigureAwait(true);
        }

        private void ItemDisplayMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplicationData.Current.LocalSettings.Values["FilePresenterDisplayMode"] = ItemDisplayMode.SelectedIndex;

            switch (ItemDisplayMode.SelectedIndex)
            {
                case 0:
                    {
                        Presenter.ItemPresenter = (Presenter.FindName("GridViewRefreshContainer") as RefreshContainer)?.Content as ListViewBase;

                        Presenter.GridViewControl.ItemTemplate = Presenter.TileDataTemplate;
                        break;
                    }
                case 1:
                    {
                        Presenter.ItemPresenter = (Presenter.FindName("ListViewRefreshContainer") as RefreshContainer)?.Content as ListViewBase;

                        Presenter.ListViewControl.HeaderTemplate = Presenter.ListHeaderDataTemplate;
                        Presenter.ListViewControl.ItemTemplate = Presenter.ListViewDetailDataTemplate;
                        Presenter.ListViewControl.ItemsSource = Presenter.FileCollection;
                        break;
                    }
                case 2:
                    {
                        Presenter.ItemPresenter = (Presenter.FindName("ListViewRefreshContainer") as RefreshContainer)?.Content as ListViewBase;

                        Presenter.ListViewControl.HeaderTemplate = null;
                        Presenter.ListViewControl.ItemTemplate = Presenter.ListViewSimpleDataTemplate;
                        Presenter.ListViewControl.ItemsSource = Presenter.FileCollection;
                        break;
                    }
                case 3:
                    {
                        Presenter.ItemPresenter = (Presenter.FindName("GridViewRefreshContainer") as RefreshContainer)?.Content as ListViewBase;

                        Presenter.GridViewControl.ItemTemplate = Presenter.LargeImageDataTemplate;
                        break;
                    }
                case 4:
                    {
                        Presenter.ItemPresenter = (Presenter.FindName("GridViewRefreshContainer") as RefreshContainer)?.Content as ListViewBase;

                        Presenter.GridViewControl.ItemTemplate = Presenter.MediumImageDataTemplate;
                        break;
                    }
                case 5:
                    {
                        Presenter.ItemPresenter = (Presenter.FindName("GridViewRefreshContainer") as RefreshContainer)?.Content as ListViewBase;

                        Presenter.GridViewControl.ItemTemplate = Presenter.SmallImageDataTemplate;
                        break;
                    }
            }
        }

        private void AddressBox_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Tab)
            {
                string FirstTip = AddressBox.Items.FirstOrDefault()?.ToString();

                if (!string.IsNullOrEmpty(FirstTip))
                {
                    AddressBox.Text = FirstTip;
                }

                e.Handled = true;
            }
        }

        private void AddressBox_LostFocus(object sender, RoutedEventArgs e)
        {
            AddressBox.Text = string.Empty;
            AddressButtonContainer.Visibility = Visibility.Visible;
        }

        private async void AddressButton_Click(object sender, RoutedEventArgs e)
        {
            string OriginalString = string.Join("\\", AddressButtonList.Take(AddressButtonList.IndexOf(Convert.ToString(((Button)sender).Content)) + 1).Skip(1));
            string ActualString = Path.Combine(Path.GetPathRoot(CurrentFolder.Path), OriginalString);

            if (ActualString == CurrentFolder.Path)
            {
                return;
            }

            if (WIN_Native_API.CheckIfHidden(ActualString))
            {
                QueueContentDialog Dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_ItemHidden_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };

                _ = await Dialog.ShowAsync().ConfigureAwait(false);

                return;
            }

            if (SettingControl.IsDetachTreeViewAndPresenter)
            {
                try
                {
                    StorageFolder TargetFolder = await StorageFolder.GetFolderFromPathAsync(ActualString);
                    await DisplayItemsInFolder(TargetFolder).ConfigureAwait(true);
                    await SQLite.Current.SetPathHistoryAsync(ActualString).ConfigureAwait(true);
                }
                catch
                {
                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} \r\"{ActualString}\"",
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                    };
                    _ = await dialog.ShowAsync().ConfigureAwait(true);
                }
            }
            else
            {
                if (!SettingControl.IsDisplayHiddenItem)
                {
                    PathAnalysis Analysis = new PathAnalysis(ActualString, string.Empty);
                    while (Analysis.HasNextLevel)
                    {
                        if (WIN_Native_API.CheckIfHidden(Analysis.NextFullPath()))
                        {
                            QueueContentDialog Dialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_NeedOpenHiddenSwitch_Content"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                            };

                            _ = await Dialog.ShowAsync().ConfigureAwait(false);
                            return;
                        }
                    }
                }

                if (ActualString.StartsWith((FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path))
                {
                    if ((await FolderTree.RootNodes[0].GetChildNodeAsync(new PathAnalysis(ActualString, (FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path)).ConfigureAwait(true)) is TreeViewNode TargetNode)
                    {
                        await DisplayItemsInFolder(TargetNode).ConfigureAwait(true);

                        await SQLite.Current.SetPathHistoryAsync(ActualString).ConfigureAwait(true);
                    }
                    else
                    {
                        QueueContentDialog dialog = new QueueContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                            Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} \r\"{ActualString}\"",
                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                        };
                        _ = await dialog.ShowAsync().ConfigureAwait(true);
                    }
                }
                else
                {
                    try
                    {
                        StorageFolder Folder = await StorageFolder.GetFolderFromPathAsync(ActualString);

                        await OpenTargetFolder(Folder).ConfigureAwait(true);

                        await SQLite.Current.SetPathHistoryAsync(ActualString).ConfigureAwait(true);
                    }
                    catch
                    {
                        QueueContentDialog dialog = new QueueContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                            Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} \r\"{ActualString}\"",
                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                        };
                        _ = await dialog.ShowAsync().ConfigureAwait(true);
                    }
                }
            }
        }

        private async void AddressExtention_Click(object sender, RoutedEventArgs e)
        {
            Button Btn = sender as Button;
            TextBlock StateText = Btn.Content as TextBlock;

            AddressExtentionList.Clear();

            string OriginalString = string.Join("\\", AddressButtonList.Take(AddressButtonList.IndexOf(Convert.ToString(Btn.FindParentOfType<StackPanel>()?.FindChildOfName<Button>("AddressButton")?.Content)) + 1).Skip(1));
            string ActualString = Path.Combine(Path.GetPathRoot(CurrentFolder.Path), OriginalString);

            List<string> ItemList = WIN_Native_API.GetStorageItemsPath(ActualString, SettingControl.IsDisplayHiddenItem, ItemFilters.Folder);

            foreach (string SubFolderName in ItemList.Select((Item) => Path.GetFileName(Item)))
            {
                AddressExtentionList.Add(SubFolderName);
            }

            if (AddressExtentionList.Count != 0)
            {
                StateText.RenderTransformOrigin = new Point(0.55, 0.6);
                await StateText.Rotate(90, duration: 150).StartAsync().ConfigureAwait(true);

                FlyoutBase.SetAttachedFlyout(Btn, AddressExtentionFlyout);
                FlyoutBase.ShowAttachedFlyout(Btn);
            }
        }

        private async void AddressExtentionFlyout_Closing(FlyoutBase sender, FlyoutBaseClosingEventArgs args)
        {
            AddressExtentionList.Clear();

            await ((sender.Target as Button).Content as FrameworkElement).Rotate(0, duration: 150).StartAsync().ConfigureAwait(false);
        }

        private async void AddressExtensionSubFolderList_ItemClick(object sender, ItemClickEventArgs e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                AddressExtentionFlyout.Hide();
            });

            if (!string.IsNullOrEmpty(e.ClickedItem.ToString()))
            {
                string OriginalString = string.Join("\\", AddressButtonList.Take(AddressButtonList.IndexOf(Convert.ToString(AddressExtentionFlyout.Target.FindParentOfType<StackPanel>()?.FindChildOfName<Button>("AddressButton")?.Content)) + 1).Skip(1));
                string ActualString = Path.Combine(Path.GetPathRoot(CurrentFolder.Path), OriginalString);

                string TargetPath = Path.Combine(ActualString, e.ClickedItem.ToString());

                if (WIN_Native_API.CheckIfHidden(TargetPath))
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_ItemHidden_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };

                    _ = await Dialog.ShowAsync().ConfigureAwait(false);
                    return;
                }

                if (SettingControl.IsDetachTreeViewAndPresenter)
                {
                    try
                    {
                        StorageFolder TargetFolder = await StorageFolder.GetFolderFromPathAsync(TargetPath);

                        await DisplayItemsInFolder(TargetFolder).ConfigureAwait(true);
                    }
                    catch
                    {
                        QueueContentDialog dialog = new QueueContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                            Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} \r\"{TargetPath}\"",
                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                        };
                        _ = await dialog.ShowAsync().ConfigureAwait(true);
                    }
                }
                else
                {
                    if (TargetPath.StartsWith((FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path))
                    {
                        TreeViewNode TargetNode = await FolderTree.RootNodes[0].GetChildNodeAsync(new PathAnalysis(TargetPath, (FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path)).ConfigureAwait(true);
                        if (TargetNode != null)
                        {
                            await DisplayItemsInFolder(TargetNode).ConfigureAwait(true);
                        }
                        else
                        {
                            QueueContentDialog dialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} \r\"{TargetPath}\"",
                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                            };
                            _ = await dialog.ShowAsync().ConfigureAwait(true);
                        }
                    }
                    else
                    {
                        try
                        {
                            StorageFolder Folder = await StorageFolder.GetFolderFromPathAsync(TargetPath);

                            await OpenTargetFolder(Folder).ConfigureAwait(true);
                        }
                        catch
                        {
                            QueueContentDialog dialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} \r\"{TargetPath}\"",
                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                            };
                            _ = await dialog.ShowAsync().ConfigureAwait(true);
                        }
                    }
                }
            }
        }

        private async void AddressButton_Drop(object sender, DragEventArgs e)
        {
            string OriginalString = string.Join("\\", AddressButtonList.Take(AddressButtonList.IndexOf(Convert.ToString(((Button)sender).Content)) + 1).Skip(1));
            string ActualPath = Path.Combine(Path.GetPathRoot(CurrentFolder.Path), OriginalString);

            bool IsHiddenTarget = false;

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                IsHiddenTarget = WIN_Native_API.CheckIfHidden(ActualPath);
            });

            if (IsHiddenTarget)
            {
                QueueContentDialog Dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_ItemHidden_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };

                _ = await Dialog.ShowAsync().ConfigureAwait(false);
                return;
            }

            if (Interlocked.Exchange(ref DropLockResource, 1) == 0)
            {
                try
                {
                    if (e.DataView.Contains(StandardDataFormats.Html))
                    {
                        string Html = await e.DataView.GetHtmlFormatAsync();
                        string Fragment = HtmlFormatHelper.GetStaticFragment(Html);

                        HtmlDocument Document = new HtmlDocument();
                        Document.LoadHtml(Fragment);
                        HtmlNode HeadNode = Document.DocumentNode.SelectSingleNode("/head");

                        if (HeadNode?.InnerText == "RX-Explorer-TransferNotStorageItem")
                        {
                            HtmlNodeCollection BodyNode = Document.DocumentNode.SelectNodes("/p");
                            List<string> LinkItemsPath = BodyNode.Select((Node) => Node.InnerText).ToList();

                            StorageFolder TargetFolder = await StorageFolder.GetFolderFromPathAsync(ActualPath);

                            switch (e.AcceptedOperation)
                            {
                                case DataPackageOperation.Copy:
                                    {
                                        await LoadingActivation(true, Globalization.GetString("Progress_Tip_Copying")).ConfigureAwait(true);

                                    Retry:
                                        try
                                        {
                                            await FullTrustProcessController.Current.CopyAsync(LinkItemsPath, TargetFolder.Path, (s, arg) =>
                                            {
                                                if (ProBar.Value < arg.ProgressPercentage)
                                                {
                                                    ProBar.IsIndeterminate = false;
                                                    ProBar.Value = arg.ProgressPercentage;
                                                }
                                            }).ConfigureAwait(true);
                                        }
                                        catch (FileNotFoundException)
                                        {
                                            QueueContentDialog Dialog = new QueueContentDialog
                                            {
                                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                Content = Globalization.GetString("QueueDialog_CopyFailForNotExist_Content"),
                                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                            };

                                            _ = await Dialog.ShowAsync().ConfigureAwait(true);
                                        }
                                        catch (InvalidOperationException)
                                        {
                                            QueueContentDialog dialog = new QueueContentDialog
                                            {
                                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                Content = Globalization.GetString("QueueDialog_UnauthorizedPaste_Content"),
                                                PrimaryButtonText = Globalization.GetString("Common_Dialog_GrantButton"),
                                                CloseButtonText = Globalization.GetString("Common_Dialog_CancelButton")
                                            };

                                            if (await dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                                            {
                                                if (await FullTrustProcessController.Current.SwitchToAdminMode().ConfigureAwait(true))
                                                {
                                                    goto Retry;
                                                }
                                                else
                                                {
                                                    QueueContentDialog ErrorDialog = new QueueContentDialog
                                                    {
                                                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                        Content = Globalization.GetString("QueueDialog_DenyElevation_Content"),
                                                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                                    };

                                                    _ = await ErrorDialog.ShowAsync().ConfigureAwait(true);
                                                }
                                            }
                                        }
                                        catch (Exception)
                                        {
                                            QueueContentDialog dialog = new QueueContentDialog
                                            {
                                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                Content = Globalization.GetString("QueueDialog_CopyFailUnexpectError_Content"),
                                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                            };

                                            _ = await dialog.ShowAsync().ConfigureAwait(true);
                                        }

                                        break;
                                    }
                                case DataPackageOperation.Move:
                                    {
                                        if (LinkItemsPath.All((Item) => Path.GetDirectoryName(Item) == ActualPath))
                                        {
                                            return;
                                        }

                                        await LoadingActivation(true, Globalization.GetString("Progress_Tip_Moving")).ConfigureAwait(true);

                                    Retry:
                                        try
                                        {
                                            await FullTrustProcessController.Current.MoveAsync(LinkItemsPath, TargetFolder.Path, (s, arg) =>
                                            {
                                                if (ProBar.Value < arg.ProgressPercentage)
                                                {
                                                    ProBar.IsIndeterminate = false;
                                                    ProBar.Value = arg.ProgressPercentage;
                                                }
                                            }).ConfigureAwait(true);
                                        }
                                        catch (FileNotFoundException)
                                        {
                                            QueueContentDialog Dialog = new QueueContentDialog
                                            {
                                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                Content = Globalization.GetString("QueueDialog_MoveFailForNotExist_Content"),
                                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                            };

                                            _ = await Dialog.ShowAsync().ConfigureAwait(true);
                                        }
                                        catch (FileCaputureException)
                                        {
                                            QueueContentDialog dialog = new QueueContentDialog
                                            {
                                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                Content = Globalization.GetString("QueueDialog_Item_Captured_Content"),
                                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                            };

                                            _ = await dialog.ShowAsync().ConfigureAwait(true);
                                        }
                                        catch (InvalidOperationException)
                                        {
                                            QueueContentDialog dialog = new QueueContentDialog
                                            {
                                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                Content = Globalization.GetString("QueueDialog_UnauthorizedPaste_Content"),
                                                PrimaryButtonText = Globalization.GetString("Common_Dialog_GrantButton"),
                                                CloseButtonText = Globalization.GetString("Common_Dialog_CancelButton")
                                            };

                                            if (await dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                                            {
                                                if (await FullTrustProcessController.Current.SwitchToAdminMode().ConfigureAwait(true))
                                                {
                                                    goto Retry;
                                                }
                                                else
                                                {
                                                    QueueContentDialog ErrorDialog = new QueueContentDialog
                                                    {
                                                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                        Content = Globalization.GetString("QueueDialog_DenyElevation_Content"),
                                                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                                    };

                                                    _ = await ErrorDialog.ShowAsync().ConfigureAwait(true);
                                                }
                                            }
                                        }
                                        catch (Exception)
                                        {
                                            QueueContentDialog dialog = new QueueContentDialog
                                            {
                                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                Content = Globalization.GetString("QueueDialog_MoveFailUnexpectError_Content"),
                                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                            };

                                            _ = await dialog.ShowAsync().ConfigureAwait(true);
                                        }

                                        break;
                                    }
                            }
                        }
                    }

                    if (e.DataView.Contains(StandardDataFormats.StorageItems))
                    {
                        List<IStorageItem> DragItemList = (await e.DataView.GetStorageItemsAsync()).ToList();

                        StorageFolder TargetFolder = await StorageFolder.GetFolderFromPathAsync(ActualPath);

                        switch (e.AcceptedOperation)
                        {
                            case DataPackageOperation.Copy:
                                {
                                    await LoadingActivation(true, Globalization.GetString("Progress_Tip_Copying")).ConfigureAwait(true);

                                Retry:
                                    try
                                    {
                                        await FullTrustProcessController.Current.CopyAsync(DragItemList, TargetFolder, (s, arg) =>
                                        {
                                            if (ProBar.Value < arg.ProgressPercentage)
                                            {
                                                ProBar.IsIndeterminate = false;
                                                ProBar.Value = arg.ProgressPercentage;
                                            }
                                        }).ConfigureAwait(true);

                                        if (!SettingControl.IsDetachTreeViewAndPresenter && ActualPath.StartsWith((FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path))
                                        {
                                            await FolderTree.RootNodes[0].UpdateAllSubNodeAsync().ConfigureAwait(true);
                                        }
                                    }
                                    catch (FileNotFoundException)
                                    {
                                        QueueContentDialog Dialog = new QueueContentDialog
                                        {
                                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                            Content = Globalization.GetString("QueueDialog_CopyFailForNotExist_Content"),
                                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                        };

                                        _ = await Dialog.ShowAsync().ConfigureAwait(true);
                                    }
                                    catch (InvalidOperationException)
                                    {
                                        QueueContentDialog dialog = new QueueContentDialog
                                        {
                                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                            Content = Globalization.GetString("QueueDialog_UnauthorizedPaste_Content"),
                                            PrimaryButtonText = Globalization.GetString("Common_Dialog_GrantButton"),
                                            CloseButtonText = Globalization.GetString("Common_Dialog_CancelButton")
                                        };

                                        if (await dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                                        {
                                            if (await FullTrustProcessController.Current.SwitchToAdminMode().ConfigureAwait(true))
                                            {
                                                goto Retry;
                                            }
                                            else
                                            {
                                                QueueContentDialog ErrorDialog = new QueueContentDialog
                                                {
                                                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                    Content = Globalization.GetString("QueueDialog_DenyElevation_Content"),
                                                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                                };

                                                _ = await ErrorDialog.ShowAsync().ConfigureAwait(true);
                                            }
                                        }
                                    }
                                    catch (Exception)
                                    {
                                        QueueContentDialog dialog = new QueueContentDialog
                                        {
                                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                            Content = Globalization.GetString("QueueDialog_CopyFailUnexpectError_Content"),
                                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                        };

                                        _ = await dialog.ShowAsync().ConfigureAwait(true);
                                    }

                                    break;
                                }
                            case DataPackageOperation.Move:
                                {
                                    if (DragItemList.Select((Item) => Item.Path).All((Item) => Path.GetDirectoryName(Item) == ActualPath))
                                    {
                                        return;
                                    }

                                    await LoadingActivation(true, Globalization.GetString("Progress_Tip_Moving")).ConfigureAwait(true);

                                Retry:
                                    try
                                    {
                                        await FullTrustProcessController.Current.MoveAsync(DragItemList, TargetFolder, (s, arg) =>
                                        {
                                            if (ProBar.Value < arg.ProgressPercentage)
                                            {
                                                ProBar.IsIndeterminate = false;
                                                ProBar.Value = arg.ProgressPercentage;
                                            }
                                        }).ConfigureAwait(true);

                                        if (!SettingControl.IsDetachTreeViewAndPresenter && ActualPath.StartsWith((FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path))
                                        {
                                            await FolderTree.RootNodes[0].UpdateAllSubNodeAsync().ConfigureAwait(true);
                                        }
                                    }
                                    catch (FileNotFoundException)
                                    {
                                        QueueContentDialog Dialog = new QueueContentDialog
                                        {
                                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                            Content = Globalization.GetString("QueueDialog_MoveFailForNotExist_Content"),
                                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                        };

                                        _ = await Dialog.ShowAsync().ConfigureAwait(true);
                                    }
                                    catch (FileCaputureException)
                                    {
                                        QueueContentDialog dialog = new QueueContentDialog
                                        {
                                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                            Content = Globalization.GetString("QueueDialog_Item_Captured_Content"),
                                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                        };

                                        _ = await dialog.ShowAsync().ConfigureAwait(true);
                                    }
                                    catch (InvalidOperationException)
                                    {
                                        QueueContentDialog dialog = new QueueContentDialog
                                        {
                                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                            Content = Globalization.GetString("QueueDialog_UnauthorizedPaste_Content"),
                                            PrimaryButtonText = Globalization.GetString("Common_Dialog_GrantButton"),
                                            CloseButtonText = Globalization.GetString("Common_Dialog_CancelButton")
                                        };

                                        if (await dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                                        {
                                            if (await FullTrustProcessController.Current.SwitchToAdminMode().ConfigureAwait(true))
                                            {
                                                goto Retry;
                                            }
                                            else
                                            {
                                                QueueContentDialog ErrorDialog = new QueueContentDialog
                                                {
                                                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                    Content = Globalization.GetString("QueueDialog_DenyElevation_Content"),
                                                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                                };

                                                _ = await ErrorDialog.ShowAsync().ConfigureAwait(true);
                                            }
                                        }
                                    }
                                    catch (Exception)
                                    {
                                        QueueContentDialog dialog = new QueueContentDialog
                                        {
                                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                            Content = Globalization.GetString("QueueDialog_MoveFailUnexpectError_Content"),
                                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                        };

                                        _ = await dialog.ShowAsync().ConfigureAwait(true);
                                    }

                                    break;
                                }
                        }
                    }
                }
                finally
                {
                    await LoadingActivation(false).ConfigureAwait(true);
                    e.Handled = true;
                    _ = Interlocked.Exchange(ref DropLockResource, 0);
                }
            }
        }

        private void AddressButton_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems) || e.DataView.Contains(StandardDataFormats.Html))
            {
                if (e.Modifiers.HasFlag(DragDropModifiers.Control))
                {
                    e.AcceptedOperation = DataPackageOperation.Copy;
                    e.DragUIOverride.Caption = $"{Globalization.GetString("Drag_Tip_CopyTo")} {(e.OriginalSource as Button).Content}";
                }
                else
                {
                    e.AcceptedOperation = DataPackageOperation.Move;
                    e.DragUIOverride.Caption = $"{Globalization.GetString("Drag_Tip_MoveTo")} {(e.OriginalSource as Button).Content}";
                }

                e.DragUIOverride.IsContentVisible = true;
                e.DragUIOverride.IsCaptionVisible = true;
            }
            else
            {
                e.AcceptedOperation = DataPackageOperation.None;
            }
        }

        private async void FolderTree_Holding(object sender, Windows.UI.Xaml.Input.HoldingRoutedEventArgs e)
        {
            if (e.HoldingState == Windows.UI.Input.HoldingState.Started)
            {
                if ((e.OriginalSource as FrameworkElement)?.DataContext is TreeViewNode Node)
                {
                    FolderTree.ContextFlyout = RightTabFlyout;

                    await DisplayItemsInFolder(Node).ConfigureAwait(true);

                    if (FolderTree.RootNodes.Contains(CurrentNode))
                    {
                        FolderDelete.IsEnabled = false;
                        FolderRename.IsEnabled = false;
                        FolderAdd.IsEnabled = false;
                    }
                    else
                    {
                        FolderDelete.IsEnabled = true;
                        FolderRename.IsEnabled = true;
                        FolderAdd.IsEnabled = true;
                    }
                }
                else
                {
                    FolderTree.ContextFlyout = null;
                }
            }
        }

        public void Dispose()
        {
            AddressButtonList.Clear();

            FolderTree.RootNodes.Clear();

            Presenter.FileCollection.Clear();
            Presenter.HasFile.Visibility = Visibility.Collapsed;

            Application.Current.Suspending -= Current_Suspending;
            Application.Current.Resuming -= Current_Resuming;
            Frame.Navigated -= Frame_Navigated;

            RecordIndex = 0;

            GoAndBackRecord.Clear();

            IsBackOrForwardAction = false;
            GoBackRecord.IsEnabled = false;
            GoForwardRecord.IsEnabled = false;
            GoParentFolder.IsEnabled = false;

            CurrentNode = null;
            CurrentFolder = null;

            EnterLock.Dispose();
            AreaWatcher.Dispose();
        }

        private void Presenter_PointerWheelChanged(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            Frame Frame = sender as Frame;

            int Delta = e.GetCurrentPoint(Frame).Properties.MouseWheelDelta;

            if (Window.Current.CoreWindow.GetKeyState(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down))
            {
                if (Delta > 0)
                {
                    if (ItemDisplayMode.SelectedIndex > 0)
                    {
                        ItemDisplayMode.SelectedIndex -= 1;
                    }
                }
                else
                {
                    if (ItemDisplayMode.SelectedIndex < ItemDisplayMode.Items.Count - 1)
                    {
                        ItemDisplayMode.SelectedIndex += 1;
                    }
                }

                e.Handled = true;
            }
        }
    }
}
