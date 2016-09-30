using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.TeamFoundation.Build.Client;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;

namespace AutoMergeTool
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private MergeType myRefreshedMergeType = MergeType.NONE;
        private const int Minutes = 120;
        private const string FIShelvesetName = "FI merge";
        private DateTime myStartTime;
        private DateTime myEndTime;
        private DispatcherTimer myTimer;
        private readonly string myTfsServer;
        private readonly string myAdminMail;
        private readonly string myUsersMail;


        public MainWindow()
        {
            InitializeComponent();


            this.TbWorkspacePath.Text = ConfigurationManager.AppSettings["WorkspacePath"];
            this.TbWorkspacePath.ToolTip = "A path to your existing workspace. Be sure that it has set up mapping rules for both branches.";
            this.TbUpperBranch.Text = ConfigurationManager.AppSettings["UpperBranch"];
            this.TbUpperBranch.ToolTip = "A branch which is more closely to Main branch.";
            this.TbTeamBranch.Text = ConfigurationManager.AppSettings["TeamBranch"];
            this.TbTeamBranch.ToolTip = "A branch in which you are wokring on.";
            this.TbBuild.Text = ConfigurationManager.AppSettings["BuildDefinition"];
            this.TbBuild.ToolTip =
                "A build name which you want to trigger after successfull FI merge (usage for dlls upload to a drop folder)).";
            this.TbWildcard.Text = ConfigurationManager.AppSettings["WildcardString"];
            this.TbWildcard.ToolTip = "A string which is using for identifying suitable changeset in upper branch.";

            myTfsServer = ConfigurationManager.AppSettings["TfsServer"];
            myAdminMail = ConfigurationManager.AppSettings["ResponsiblePersonMail"];
            myUsersMail = ConfigurationManager.AppSettings["UsersMail"];
        }

        #region MainWindow handling methods

        private void OnRefreshFIButton_Click(object sender, RoutedEventArgs e)
        {
            CreateAndStartProcessForDataGridUpdate(MergeType.FI);
            this.myRefreshedMergeType = MergeType.FI;
        }

        private void OnMergeFIButton_Click(object sender, RoutedEventArgs e)
        {
            if (myRefreshedMergeType == MergeType.NONE || myRefreshedMergeType != MergeType.FI)
            {
                this.WriteToConsole("The FI refresh has to be triggered first.");
                return;
            }
            CreateAndStartProcessForMerge(MergeType.FI, false);
        }
        private void OnRefreshRIButton_Click(object sender, RoutedEventArgs e)
        {
            CreateAndStartProcessForDataGridUpdate(MergeType.RI);
            this.myRefreshedMergeType = MergeType.RI;
        }

        private void OnMergeRIButton_Click(object sender, RoutedEventArgs e)
        {
            if (myRefreshedMergeType == MergeType.NONE || myRefreshedMergeType != MergeType.RI)
            {
                this.WriteToConsole("The RI refresh has to be triggered first.");
                return;
            }
            CreateAndStartProcessForMerge(MergeType.RI, false);
        }

        private void OnSaveButton_Click(object sender, RoutedEventArgs e)
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            config.AppSettings.Settings["WorkspacePath"].Value = this.TbWorkspacePath.Text;
            config.AppSettings.Settings["UpperBranch"].Value = this.TbUpperBranch.Text;
            config.AppSettings.Settings["TeamBranch"].Value = this.TbTeamBranch.Text;
            config.AppSettings.Settings["BuildDefinition"].Value = this.TbBuild.Text;
            config.AppSettings.Settings["WildcardString"].Value = this.TbWildcard.Text;
            config.Save(ConfigurationSaveMode.Modified);
        }

        private void OnCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            SetButtonsState(false);

            myEndTime = DateTime.UtcNow.AddMinutes(1);

            this.myTimer = new DispatcherTimer();
            this.myTimer.Tick += DispatcherMyTimerTick;
            this.myTimer.Interval = new TimeSpan(0, 0, 1);
            this.myTimer.Start();
        }

        private void OnCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            SetButtonsState(true);

            this.myTimer.Stop();
            this.myTimer.Tick -= DispatcherMyTimerTick;
            this.CbAutomaticalMerge.Content = "Automatical merge";
        }

        private void OnDataGrid_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (this.myRefreshedMergeType == MergeType.RI && this.DgChangesets.SelectedItems.Count > 1)
            {
                var oldestChangeset = this.DgChangesets.SelectedItems.OfType<Changeset>().OrderBy(o => o.ID).First();
                var latestChangeset = this.DgChangesets.SelectedItems.OfType<Changeset>().OrderBy(o => o.ID).Last();
                var newItemsForSelection = this.DgChangesets.Items.OfType<Changeset>().Where(x => x.ID <= latestChangeset.ID && x.ID >= oldestChangeset.ID).Select(x => x.ID);
                var rowIndex = new List<int>();
                for (int i = 0; i < this.DgChangesets.Items.Count; i++)
                {
                    var id = (this.DgChangesets.Items[i] as Changeset).ID;
                    if (newItemsForSelection.Contains(id))
                    {
                        rowIndex.Add(i);
                    }
                }
                SelectRowByIndexes(this.DgChangesets, rowIndex.ToArray());
            } else if (this.myRefreshedMergeType == MergeType.FI)
            {
                var oldestChangeset = this.DgChangesets.Items.OfType<Changeset>().OrderBy(o => o.ID).First();                
                var newItemsForSelection = this.DgChangesets.Items.OfType<Changeset>().Where(x => x.ID <= this.DgChangesets.SelectedItems.OfType<Changeset>().First().ID && x.ID >= oldestChangeset.ID).Select(x => x.ID);
                var rowIndex = new List<int>();
                for (int i = 0; i < this.DgChangesets.Items.Count; i++)
                {
                    var id = (this.DgChangesets.Items[i] as Changeset).ID;
                    if (newItemsForSelection.Contains(id))
                    {
                        rowIndex.Add(i);
                    }
                }
                SelectRowByIndexes(this.DgChangesets, rowIndex.OrderByDescending(i => i).ToArray());
            }
        }

        #endregion

        #region Execution processes

        private void CreateAndStartProcessForDataGridUpdate(MergeType mergeType)
        {
            var updateDgThread = new Thread(() => UpdateDataGrid(mergeType, true));
            updateDgThread.Start();
        }

        private void CreateAndStartProcessForMerge(MergeType mergeType, bool automerge)
        {
            if (this.DgChangesets.SelectedItem != null || automerge)
            {
                var updateDgThread = new Thread(() => PerformMerge(mergeType, automerge));
                updateDgThread.Start();
            }
            else
            {
                this.WriteToConsole("For the merge operation you have to select a changeset.");
            }
        }

        #endregion

        #region DataGrid update

        private void UpdateDataGrid(MergeType mergeType, bool disableButtons)
        {
            Dispatcher.Invoke(() =>
            {
                this.WriteToConsole(string.Format("Looking for {0} merge candidates", mergeType));
                this.ProgressBar.Visibility = Visibility.Visible;
                if (disableButtons) SetButtonsState(false);
                this.DgChangesets.IsEnabled = false;
                this.DgChangesets.IsReadOnly = false;
            });

            string source = "";
            string target = "";
            Dispatcher.Invoke(() =>
            {
                if (mergeType == MergeType.FI)
                {
                    source = this.TbUpperBranch.Text;
                    target = this.TbTeamBranch.Text;
                }
                else
                {
                    source = this.TbTeamBranch.Text;
                    target = this.TbUpperBranch.Text;
                }
            });

            var changesets = this.GetChangesetsEligibleForMerge(source, target);

            Dispatcher.Invoke(() =>
            {
                this.DgChangesets.ItemsSource = changesets;
                SortDataGrid(this.DgChangesets);
                if (disableButtons) SetButtonsState(true);
                this.ProgressBar.Visibility = Visibility.Hidden;
                this.DgChangesets.IsEnabled = true;
                this.DgChangesets.IsReadOnly = true;
                this.TbConsole.AppendText("done" + Environment.NewLine);
            });
        }

        private void SortDataGrid(DataGrid dataGrid, int columnIndex = 0, ListSortDirection sortDirection = ListSortDirection.Descending)
        {
            var column = dataGrid.Columns[columnIndex];

            // Clear current sort descriptions
            dataGrid.Items.SortDescriptions.Clear();

            // Add the new sort description
            dataGrid.Items.SortDescriptions.Add(new SortDescription(column.SortMemberPath, sortDirection));

            // Apply sort
            foreach (var col in dataGrid.Columns)
            {
                col.SortDirection = null;
            }
            column.SortDirection = sortDirection;

            // Refresh items to display sort
            dataGrid.Items.Refresh();
        }

        #endregion

        #region Merge helper methods

        private List<Changeset> GetChangesetsEligibleForMerge(string source, string target)
        {
            var eligibleMergeItems = new List<Changeset>();

            using (var tfs = new TfsTeamProjectCollection(new Uri(myTfsServer)))
            {
                var vcs = (VersionControlServer)tfs.GetService(typeof(VersionControlServer));

                foreach (var mergeCandidate in vcs.GetMergeCandidates(source, target, RecursionType.Full))
                {
                    eligibleMergeItems.Add(new Changeset()
                    {
                        ID = mergeCandidate.Changeset.ChangesetId,
                        User = mergeCandidate.Changeset.Owner,
                        Date = mergeCandidate.Changeset.CreationDate,
                        Comment = mergeCandidate.Changeset.Comment
                    });
                }
            }

            eligibleMergeItems.OrderBy(o => o.ID).ToList();

            return eligibleMergeItems;
        }

        private void PerformMerge(MergeType mergeType, bool automerge)
        {
            using (var tfs = new TfsTeamProjectCollection(new Uri(myTfsServer)))
            {
                Workspace workspace = null;
                ChangesetVersionSpec changesetFrom = null;
                ChangesetVersionSpec changesetTo = null;
                string source = "";
                string target = "";

                SetButtonsState(false);
                Dispatcher.Invoke(() =>
                {
                    this.CbAutomaticalMerge.IsEnabled = false;
                });

                if (automerge)
                {
                    UpdateDataGrid(MergeType.FI, false);
                }

                Dispatcher.Invoke(() =>
                {
                    var changesets = automerge ? this.DgChangesets.Items : this.DgChangesets.SelectedItems;
                    var vcs = (VersionControlServer)tfs.GetService(typeof(VersionControlServer));
                    workspace = vcs.GetWorkspace(this.TbWorkspacePath.Text);
                    var changesetFromID = changesets.OfType<Changeset>().OrderBy(o => o.ID).First().ID;
                    var changesetToDummy = automerge
                        ? changesets.OfType<Changeset>()
                            .OrderByDescending(o => o.ID)
                            .FirstOrDefault(x => x.Comment.Contains(this.TbWildcard.Text))
                        : changesets.OfType<Changeset>().OrderBy(o => o.ID).Last();
                    var changesetToID = changesetToDummy == null ? -1 : changesetToDummy.ID;
                    if (automerge && changesetToID == -1)
                    {
                        this.WriteToConsole(
                            string.Format(
                                "The '{0}' string was not found in any changeset candidate. Next try will be at {1}",
                                this.TbWildcard.Text, DateTime.Now.AddMinutes(Minutes)));
                        myEndTime = DateTime.UtcNow.AddMinutes(Minutes);
                        this.myTimer.Start();
                        SetButtonsState(true);
                        this.CbAutomaticalMerge.IsEnabled = true;
                        return;
                    }
                    changesetFrom = new ChangesetVersionSpec(changesetFromID);
                    changesetTo = new ChangesetVersionSpec(changesetToID);

                    if (mergeType == MergeType.FI)
                    {
                        source = this.TbUpperBranch.Text;
                        target = this.TbTeamBranch.Text;
                    }
                    else
                    {
                        source = this.TbTeamBranch.Text;
                        target = this.TbUpperBranch.Text;
                    }

                    this.WriteToConsole(
                        string.Format(
                            "Merge operation for the changesets in range: {0}->{1} started at {2}",
                            changesetFromID, changesetToID, DateTime.Now));
                });

                if (changesetFrom == null || changesetTo == null)
                {
                    return;
                }

                // perform the merge
                var result = workspace.Merge(source, target, changesetFrom,
                    changesetTo, LockLevel.None, RecursionType.Full, MergeOptions.None);

                // notify about result
                this.WriteToConsole("HaveResolvableWarnings: " + result.HaveResolvableWarnings);
                this.WriteToConsole("NoActionNeeded: " + result.NoActionNeeded);
                this.WriteToConsole("NumConflicts: " + result.NumConflicts);
                this.WriteToConsole("NumFailures: " + result.NumFailures);
                this.WriteToConsole("NumFiles: " + result.NumFiles);
                this.WriteToConsole("NumResolvedConflicts: " + result.NumResolvedConflicts);
                this.WriteToConsole("NumUpdated: " + result.NumUpdated);
                this.WriteToConsole("NumWarnings: " + result.NumWarnings);


                if (result.NumConflicts > 0)
                {
                    this.WriteToConsole("The merge was canceled because of existing conflicts:");
                    this.WriteToConsole(String.Join(Environment.NewLine, result.GetFailures().Select(x => x.Message).ToArray()));
                    SetButtonsState(true);
                    if (!automerge)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            this.CbAutomaticalMerge.IsEnabled = true;
                        });
                    }
                    return;
                }

                //if (result.NumFailures > 0)
                //{
                //    this.WriteToConsole("During merge was found failures:");
                //    this.WriteToConsole(String.Join(Environment.NewLine, result.forma Select(x => x.Message).ToArray()));
                //    SetButtonsState(true);
                //    if (!automerge)
                //    {
                //        Dispatcher.Invoke(() =>
                //        {
                //            this.CbAutomaticalMerge.IsEnabled = true;
                //        });
                //    }
                //    return;
                //}

                // fill checkin parameters
                WorkspaceCheckInParameters parameters = null;
                string automergeComment = automerge ? "***AutoMerge***" : "";
                if (mergeType == MergeType.FI)
                {
                    string checkinComment = string.Format("FI from {0} up to {1} {2}", source.Split('/').Last(),
                        changesetTo.ChangesetId, automergeComment);
                    parameters = new WorkspaceCheckInParameters(workspace.GetPendingChanges(), checkinComment);

                    var vcs = (VersionControlServer) tfs.GetService(typeof(VersionControlServer));
                    var shelveset = new Shelveset(vcs, FIShelvesetName, workspace.OwnerName) {Comment = checkinComment};
                    workspace.Shelve(shelveset, workspace.GetPendingChanges(), ShelvingOptions.Replace);
                }
                else
                {
                    parameters = new WorkspaceCheckInParameters(workspace.GetPendingChanges(),
                        string.Format("RI from {0} in range {1}->{2}",
                            source.Split('/').Last(), changesetFrom.ChangesetId, changesetTo.ChangesetId));
                }

                // perform the checkin
                var checkedIn = PerformCheckIn(workspace, parameters, mergeType);

                Dispatcher.Invoke(() =>
                {
                    if (!checkedIn && automerge)
                    {
                        this.WriteToConsole("Because of previous failure the Automerge is disabled");
                        this.CbAutomaticalMerge.IsChecked = false;
                    }

                    if (checkedIn && automerge)
                    {
                        myEndTime = DateTime.UtcNow.AddMinutes(Minutes);
                        this.myTimer.Start();
                    }
                });

                SetButtonsState(true);
                Dispatcher.Invoke(() =>
                {
                    this.CbAutomaticalMerge.IsEnabled = true;
                });
            }
        }

        #endregion

        #region Checkin

        private bool PerformCheckIn(Workspace workspace, WorkspaceCheckInParameters parameters, MergeType mergeType)
        {
            using (var tfs = new TfsTeamProjectCollection(new Uri(myTfsServer)))
            {
                var buildServer = tfs.GetService<IBuildServer>();
                try
                {
                    var result = workspace.EvaluateCheckin(CheckinEvaluationOptions.All,
                        parameters.PendingChanges.ToArray(), parameters.PendingChanges.ToArray(), parameters.Comment,
                        null,
                        null);

                    if (result.PolicyFailures.Any())
                    {
                        this.WriteToConsole("The checkin was canceled because of existing policy failures:");
                        this.WriteToConsole(String.Join(Environment.NewLine,
                            result.PolicyFailures.Select(x => x.Message).ToArray()));
                        return false;
                    }

                    if (mergeType == MergeType.RI)
                    {
                        var checkinChs = workspace.CheckIn(parameters);
                        this.WriteToConsole(string.Format("The checkin was commited with changeset: {0} ", checkinChs));
                    }
                    else
                    {
                        //Create a build request for the gated check-in build
                        IBuildRequest buildRequest = GetBuildDefinition().CreateBuildRequest();
                        buildRequest.ShelvesetName = FIShelvesetName;
                        buildRequest.Reason = BuildReason.CheckInShelveset;

                        workspace.Undo(workspace.GetPendingChanges());

                        //Queue the build request
                        QueueBuild(buildRequest);
                    }
                }
                catch (GatedCheckinException gatedException)
                {
                    //This exception tells us that a gated check-in is required.
                    //First, we get the list of build definitions affected by the check-in
                    ICollection<KeyValuePair<string, Uri>> buildDefs = gatedException.AffectedBuildDefinitions;

                    if (buildDefs.Count == 1)
                    {
                        //If only one affected build definition exists, then we have everything we need to proceed
                        IEnumerator<KeyValuePair<string, Uri>> buildEnum = buildDefs.GetEnumerator();
                        buildEnum.MoveNext();
                        KeyValuePair<string, Uri> buildDef = buildEnum.Current;
                        Uri gatedBuildDefUri = buildDef.Value;
                        string shelvesetSpecName = gatedException.ShelvesetName;
                        string[] shelvesetTokens = shelvesetSpecName.Split(new char[] {';'});

                        //Create a build request for the gated check-in build
                        IBuildRequest buildRequest = buildServer.CreateBuildRequest(gatedBuildDefUri);
                        buildRequest.ShelvesetName = shelvesetTokens[0]; //Specify the name of the existing shelveset
                        buildRequest.Reason = BuildReason.CheckInShelveset; //Check-in the shelveset if successful 
                        buildRequest.GatedCheckInTicket = gatedException.CheckInTicket; //Associate the check-in

                        //Queue the build request
                        QueueBuild(buildRequest);
                    }
                    else
                    {
                        this.WriteToConsole("More build definitions exist for this gated-checkin.");
                        return false;
                    }
                }
                catch (CheckinException checkinException)
                {
                    this.WriteToConsole(string.Format("An exception was catched during checkin: {0} ",
                        checkinException.InnerException));
                    return false;
                }
                return true;
            }
        }

        #endregion

        #region DataGrid rows selection

        public static void SelectRowByIndexes(DataGrid dataGrid, params int[] rowIndexes)
        {
            if (!dataGrid.SelectionUnit.Equals(DataGridSelectionUnit.FullRow))
                throw new ArgumentException("The SelectionUnit of the DataGrid must be set to FullRow.");

            if (!dataGrid.SelectionMode.Equals(DataGridSelectionMode.Extended))
                throw new ArgumentException("The SelectionMode of the DataGrid must be set to Extended.");

            if (rowIndexes.Length.Equals(0) || rowIndexes.Length > dataGrid.Items.Count)
                throw new ArgumentException("Invalid number of indexes.");

            dataGrid.SelectedItems.Clear();
            foreach (int rowIndex in rowIndexes)
            {
                if (rowIndex < 0 || rowIndex > (dataGrid.Items.Count - 1))
                    throw new ArgumentException(string.Format("{0} is an invalid row index.", rowIndex));

                object item = dataGrid.Items[rowIndex]; //=Product X
                dataGrid.SelectedItems.Add(item);

                DataGridRow row = dataGrid.ItemContainerGenerator.ContainerFromIndex(rowIndex) as DataGridRow;
                if (row == null)
                {
                    dataGrid.ScrollIntoView(item);
                    row = dataGrid.ItemContainerGenerator.ContainerFromIndex(rowIndex) as DataGridRow;
                }
                if (row != null)
                {
                    DataGridCell cell = GetCell(dataGrid, row, 0);
                    if (cell != null)
                        cell.Focus();
                }
            }
        }

        public static DataGridCell GetCell(DataGrid dataGrid, DataGridRow rowContainer, int column)
        {
            if (rowContainer != null)
            {
                DataGridCellsPresenter presenter = FindVisualChild<DataGridCellsPresenter>(rowContainer);
                if (presenter == null)
                {
                    /* if the row has been virtualized away, call its ApplyTemplate() method
                     * to build its visual tree in order for the DataGridCellsPresenter
                     * and the DataGridCells to be created */
                    rowContainer.ApplyTemplate();
                    presenter = FindVisualChild<DataGridCellsPresenter>(rowContainer);
                }
                if (presenter != null)
                {
                    DataGridCell cell = presenter.ItemContainerGenerator.ContainerFromIndex(column) as DataGridCell;
                    if (cell == null)
                    {
                        /* bring the column into view
                         * in case it has been virtualized away */
                        dataGrid.ScrollIntoView(rowContainer, dataGrid.Columns[column]);
                        cell = presenter.ItemContainerGenerator.ContainerFromIndex(column) as DataGridCell;
                    }
                    return cell;
                }
            }
            return null;
        }

        public static T FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(obj, i);
                if (child != null && child is T)
                    return (T)child;
                else
                {
                    T childOfChild = FindVisualChild<T>(child);
                    if (childOfChild != null)
                        return childOfChild;
                }
            }
            return null;
        }

        #endregion

        #region Queue build

        private IBuildDefinition GetBuildDefinition()
        {
            using (var tfs = new TfsTeamProjectCollection(new Uri(myTfsServer)))
            {
                var buildServer = tfs.GetService<IBuildServer>();
                string buildName = "";
                Dispatcher.Invoke(() =>
                {
                    buildName = this.TbBuild.Text;
                });
                return buildServer.GetBuildDefinition("syngo.net", buildName, QueryOptions.Definitions);
            }
        }

        private void QueueBuild(IBuildRequest buildRequest)
        {
            using (var tfs = new TfsTeamProjectCollection(new Uri(myTfsServer)))
            {
                string buildName = "";
                SetButtonsState(false);
                Dispatcher.Invoke(() =>
                {
                    buildName = this.TbBuild.Text;
                });

                var buildServer = tfs.GetService<IBuildServer>();
                var build = buildServer.QueueBuild(buildRequest);

                this.WriteToConsole(string.Format("The build {0} was queued at {1}", buildName, DateTime.Now));
                build.WaitForBuildCompletion(new TimeSpan(0, 1, 0), new TimeSpan(1, 0, 0, 0));
                this.WriteToConsole(string.Format("The build {0} was finished at {1}", buildName, DateTime.Now));
                SetButtonsState(true);

                SendMailNotification(build);
            }
        }

        private void SendMailNotification(IQueuedBuild build)
        {
            MailMessage message = null;
            if (build.Build.Status == BuildStatus.Succeeded)
            {
                if (!string.IsNullOrEmpty(this.myUsersMail))
                {
                    message = new MailMessage("AutoMerge@tool.com", this.myUsersMail,
                        "AutoMerge tool notifikacia", string.Format(
                            "Dobehol novy FI build (chs {0}): {1}",
                            build.Build.SourceGetVersion, build.Build.DropLocation));
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(this.myAdminMail))
                {
                    message = new MailMessage("AutoMerge@tool.com", this.myAdminMail,
                        "AutoMerge tool notifikacia. Build FAILED.",
                        "FYI");
                }
            }

            if (message != null)
            {
                // send mail notification
                var smtpClient = new SmtpClient("webmail-cee.siemens.net");
                smtpClient.Send(message);
            }
        }

        #endregion

        #region Helper methods

        private void DispatcherMyTimerTick(object sender, EventArgs e)
        {
            var remainingTime = myEndTime - DateTime.UtcNow;
            if (remainingTime < TimeSpan.Zero)
            {
                this.myTimer.Stop();
                this.WriteToConsole("The AutoMerge trigger merge check.");
                CreateAndStartProcessForMerge(MergeType.FI, true);
            }
            else
            {
                this.CbAutomaticalMerge.Content = string.Format("Automatical merge: {0}:{1}", remainingTime.Minutes, remainingTime.Seconds);
            }

        }

        private void SetButtonsState(bool state)
        {
            Dispatcher.Invoke(() =>
            {
                this.BtnRefreshFI.IsEnabled = state;
                this.BtnMergeFI.IsEnabled = state;
                this.BtnRefreshRI.IsEnabled = state;
                this.BtnMergeRI.IsEnabled = state;
            });
        }

        private void WriteToConsole(string text)
        {
            Dispatcher.Invoke(() =>
            {
                this.TbConsole.AppendText(text + Environment.NewLine);
                this.TbConsole.ScrollToEnd();
            });
        }

        #endregion

        enum MergeType { FI, RI, NONE };

        private class Changeset
        {
            public int ID { get; set; }
            public string User { get; set; }
            public DateTime Date { get; set; }
            public string Comment { get; set; }
        }
    }
}
