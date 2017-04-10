﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using _3PA.Lib;

namespace _3PA.MainFeatures.Pro {

    internal class ProDeployment {

        #region Events

        /// <summary>
        /// The action to execute just after the end of a prowin process
        /// </summary>
        public Action<ProDeployment> OnExecutionEnd { private get; set; }

        /// <summary>
        /// The action to execute at the end of the process if it went well = we found a .log and the database is connected or is not mandatory
        /// </summary>
        public Action<ProDeployment> OnExecutionOk { private get; set; }

        /// <summary>
        /// The action to execute at the end of the process if something went wrong (no .log or database down)
        /// </summary>
        public Action<ProDeployment> OnExecutionFailed { private get; set; }

        #endregion

        #region Options

        /// <summary>
        /// If true, don't actually do anything, just test it
        /// </summary>
        public bool IsTestMode { get; set; }

        #endregion

        #region Public properties

        /// <summary>
        /// max step number composing this deployment
        /// </summary>
        public int MaxStep { get; private set; }

        /// <summary>
        /// Total number of steps composing this deployment
        /// 1 compil, 2 deploy compil r code, 3 step 1...
        /// </summary>
        public int TotalNumberOfSteps { get { return MaxStep + 2; } }

        /// <summary>
        /// Current deployment step
        /// </summary>
        public int CurrentStep { get; private set; }

        /// <summary>
        /// 0 -> 100% progression for the deployment
        /// </summary>
        public float ProgressionPercentage {
            get {
                float totalPerc = _proCompilation == null ? 0 : _proCompilation.CompilationProgression;
                if (CurrentStep > 0) {
                    totalPerc += CurrentStep * 100;
                }
                totalPerc += _currentStepDeployPercentage;
                return totalPerc / TotalNumberOfSteps;
            }
        }

        /// <summary>
        /// remember the time when the compilation started
        /// </summary>
        public DateTime StartingTime { get; private set; }

        /// <summary>
        /// Human readable amount of time needed for this execution
        /// </summary>
        public string TotalDeploymentTime { get; private set; }

        public bool CompilationHasFailed { get; private set; }

        public bool HasBeenCancelled {
            get { return _cancelSource.IsCancellationRequested; }
        }

        /// <summary>
        /// Get the time elapsed since the beginning of the compilation in a human readable format
        /// </summary>
        public string ElapsedTime {
            get { return Utils.ConvertToHumanTime(TimeSpan.FromMilliseconds(DateTime.Now.Subtract(StartingTime).TotalMilliseconds)); }
        }

        #endregion

        #region Private fields

        private Dictionary<int, List<FileToDeploy>> _filesToDeployPerStep = new Dictionary<int, List<FileToDeploy>>();

        private DeployProfile _currentProfile;

        private ProEnvironment.ProEnvironmentObject _proEnv;

        private volatile float _currentStepDeployPercentage;

        // Stores the current compilation info
        private ProCompilation _proCompilation;

        CancellationTokenSource _cancelSource = new CancellationTokenSource();

        private ProExecutionDeploymentHook _hookExecution;

        #endregion

        #region Life and death

        /// <summary>
        /// Constructor
        /// </summary>
        public ProDeployment(ProEnvironment.ProEnvironmentObject proEnv, DeployProfile currentProfile) {
            _proEnv = new ProEnvironment.ProEnvironmentObject(proEnv);
            _currentProfile = new DeployProfile(currentProfile);
            StartingTime = DateTime.Now;
        }

        #endregion

        #region Public

        /// <summary>
        /// Start the deployment
        /// </summary>
        public bool Start() {

            StartingTime = DateTime.Now;
            MaxStep = _proEnv.Deployer.DeployTransferRules.Count > 0 ? _proEnv.Deployer.DeployTransferRules.Max(rule => rule.Step) : 0;
            _filesToDeployPerStep.Clear();

            // new mass compilation
            _proCompilation = new ProCompilation(_proEnv) {
                // check if we need to force the compiler to only use 1 process 
                // (either because the user want to, or because we have a single user mode database)
                MonoProcess = _currentProfile.ForceSingleProcess || _proEnv.IsDatabaseSingleUser,
                NumberOfProcessesPerCore = _currentProfile.NumberProcessPerCore,
                RFilesOnly = _currentProfile.OnlyGenerateRcode
            };

            _proCompilation.OnCompilationOk += OnCompilationOk;
            _proCompilation.OnCompilationFailed += OnCompilationFailed;

            return _proCompilation.CompileFiles(GetFilesToCompileInStepZero());
        }

        /// <summary>
        /// Call this method to cancel the execution of this deployment
        /// </summary>
        public void Cancel() {
            _cancelSource.Cancel();
            if (_proCompilation != null)
                _proCompilation.CancelCompilation();
            if (_hookExecution != null)
                _hookExecution.KillProcess();
            EndOfDeployment();
        }

        #endregion

        #region To override

        /// <summary>
        /// List all the compilable files in the source directory
        /// </summary>
        protected virtual List<FileToCompile> GetFilesToCompileInStepZero() {
            return 
                _proEnv.Deployer.GetFilteredFilesList(
                    new List<string> { _currentProfile.SourceDirectory },
                    0,
                    _currentProfile.ExploreRecursively ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly,
                    Config.Instance.FilesPatternCompilable
                ).Select(s => new FileToCompile(s)).ToList();
        }

        /// <summary>
        /// List all the files that should be deployed from the source directory
        /// </summary>
        protected virtual List<FileToDeploy> GetFilesToDeployInStepOne() {
            return _proEnv.Deployer.GetFilesToDeployForStep(1,
                new List<string> { _currentProfile.SourceDirectory },
                _currentProfile.ExploreRecursively ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly
            );
        }

        /// <summary>
        /// List all the files that should be deployed from the source directory
        /// </summary>
        protected virtual List<FileToDeploy> GetFilesToDeployInStepTwoAndMore(int currentStep) {
            return _proEnv.Deployer.GetFilesToDeployForStep(currentStep,
                new List<string> { _proEnv.BaseCompilationPath },
                _currentProfile.ExploreRecursively ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly
            );
        }

        protected virtual List<FileToDeploy> Deployfiles(List<FileToDeploy> filesToDeploy) {
            return _proEnv.Deployer.DeployFiles(filesToDeploy, f => _currentStepDeployPercentage = f, _cancelSource);
        }

        #endregion

        #region Private

        /// <summary>
        /// Called when the compilation step 0 failed
        /// </summary>
        private void OnCompilationFailed(ProCompilation proCompilation) {
            if (HasBeenCancelled)
                return;

            CompilationHasFailed = true;
            EndOfDeployment();
        }

        /// <summary>
        /// Called when the compilation step 0 ended correctly
        /// </summary>
        private void OnCompilationOk(ProCompilation comp, List<FileToCompile> fileToCompiles, List<FileToDeploy> filesToDeploy) {
            if (HasBeenCancelled)
                return;

            // Make the deployment for the compilation step (0)
            _filesToDeployPerStep.Add(0, Deployfiles(filesToDeploy));

            // Make the deployment for the step 1 and >=
            ExecuteDeploymentHook(0);
        }
        
        /// <summary>
        /// Deployment for the step 1 and >=
        /// </summary>
        private void DeployStepOneAndMore(int currentStep) {
            if (HasBeenCancelled)
                return;

            _currentStepDeployPercentage = 0;
            CurrentStep = currentStep;

            if (currentStep <= MaxStep) {

                List<FileToDeploy> filesToDeploy = currentStep == 1 ? GetFilesToDeployInStepOne() : GetFilesToDeployInStepTwoAndMore(currentStep);
                _filesToDeployPerStep.Add(currentStep, Deployfiles(filesToDeploy));

                // hook
                ExecuteDeploymentHook(currentStep);

            } else {
                
                // end of the overall deployment
                EndOfDeployment();
            }
        }

        /// <summary>
        /// Execute the hook procedure for the step 0+
        /// </summary>
        private void ExecuteDeploymentHook(int currentStep) {
            if (HasBeenCancelled)
                return;

            currentStep++;

            // launch the compile process for the current file
            if (File.Exists(Config.FileDeploymentHook)) {
                _hookExecution = new ProExecutionDeploymentHook(_proEnv) {
                    DeploymentStep = currentStep - 1,
                    DeploymentSourcePath = _currentProfile.SourceDirectory,
                    NoBatch = true,
                    NeedDatabaseConnection = true
                };
                _hookExecution.OnExecutionEnd += execution => {
                    DeployStepOneAndMore(currentStep);
                };
                if (!_hookExecution.Start()) {
                    DeployStepOneAndMore(currentStep);
                }
            } else {
                DeployStepOneAndMore(currentStep);
            }
        }

        /// <summary>
        /// This method is executed when the overall execution is over
        /// </summary>
        private void EndOfDeployment() {

            TotalDeploymentTime = ElapsedTime;

            if (!HasBeenCancelled && !CompilationHasFailed) {
                if (OnExecutionOk != null)
                    OnExecutionOk(this);
            } else {
                if (OnExecutionFailed != null)
                    OnExecutionFailed(this);
            }

            if (OnExecutionEnd != null)
                OnExecutionEnd(this);
            
        }

        #endregion
        
        #region FormatDeploymentReport

        /// <summary>
        /// Generate an html report for the current deployment
        /// </summary>
        public string FormatDeploymentReport() {

            StringBuilder currentReport = new StringBuilder();

            currentReport.Append("<div class='NormalBackColor'>");
            currentReport.Append(@"
                <table class='ToolTipName' style='margin-bottom: 0px; width: 100%'>
                    <tr>
                        <td rowspan='2' style='width: 95px; padding-left: 10px'><img src='Report_64x64' width='64' height='64' /></td>
                        <td class='NotificationTitle'>Deployment report</td>
                    </tr>
                    <tr>
                        <td class='NotificationSubTitle'>" + (HasBeenCancelled ? "<img style='padding-right: 2px;' src='Warning30x30' height='25px'>Canceled by the user" : (!CompilationHasFailed ? "<img style='padding-right: 2px;' src='Ok30x30' height='25px'>Done!" : "<img style='padding-right: 2px;' src='Error30x30' height='25px'>An error has occurred...")) + @"</td>
                    </tr>
                </table>");

            currentReport.Append(@"<h2 style='margin-top: 8px; margin-bottom: 8px;'>Parameters :</h2>");

            currentReport.Append(@"                     
                <div style='margin-left: 8px; margin-right: 8px;'>
                    <table style='width: 100%' class='NormalBackColor'>
                        <tr><td style='width: 40%; padding-right: 20px'>Compilation starting time :</td><td><b>" + _proCompilation.StartingTime + @"</b></td></tr>
                        <tr><td style='padding-right: 20px'>Number of cores detected on this computer :</td><td><b>" + Environment.ProcessorCount + @" cores</b></td></tr>
                        <tr><td style='padding-right: 20px'>Number of Prowin processes used for the compilation :</td><td><b>" + _proCompilation.TotalNumberOfProcesses + @" processes</b></td></tr>
                        <tr><td style='padding-right: 20px'>Forced to mono process? :</td><td><b>" + _proCompilation.MonoProcess + (_proEnv.IsDatabaseSingleUser ? " (connected to database in single user mode!)" : "") + @"</b></td></tr>
                        <tr><td style='width: 40%; padding-right: 20px'>Total number of files being compile :</td><td><b>" + _proCompilation.NbFilesToCompile + @" files</b></td></tr>
                        <tr><td style='width: 40%; padding-right: 20px'>Source directory :</td><td><b>" + _currentProfile.SourceDirectory.ToHtmlLink() + @"</b></td></tr>
                        <tr><td style='width: 40%; padding-right: 20px'>Target deployment directory :</td><td><b>" + _proEnv.BaseCompilationPath.ToHtmlLink() + @"</b></td></tr>
                    </table>           
                </div>");

            currentReport.Append(@"<h2 style='margin-top: 8px; margin-bottom: 8px;'>Results :</h2>");

            if (HasBeenCancelled) {
                // the process has been canceled
                currentReport.Append(@"<div><img style='padding-right: 20px; padding-left: 5px;' src='Warning30x30' height='15px'>The deployment has been canceled by the user</div>");

            } else if (CompilationHasFailed) {

                // provide info on the possible error!
                currentReport.Append(@"<div><img style='padding-right: 20px; padding-left: 5px;' src='Error30x30' height='15px'>At least one process has ended in error, the compilation has been canceled</div>");

                if (_proCompilation.CompilationFailedOnMaxUser) {
                    currentReport.Append(@"<div><img style='padding-right: 20px; padding-left: 5px;' src='Help' height='15px'>One or more processes started for this compilation tried to connect to the database and failed because the maximum number of connection has been reached (error 748). To correct this problem, you can either :<br><li>reduce the number of processes to use for each core of your computer</li><li>or increase the maximum of connections for your database (-n parameter in the PROSERVE command)</li></div>");
                }
            }
            

            var listLinesCompilation = new List<Tuple<int, string>>();
            StringBuilder line = new StringBuilder();

            var totalDeployedFiles = 0;
            var nbDeploymentError = 0;
            var nbCompilationError = 0;
            var nbCompilationWarning = 0;

            // compilation errors
            foreach (var fileInError in _proCompilation.ListFilesToCompile.Where(file => file.Errors != null)) {
                bool hasError = fileInError.Errors.Exists(error => error.Level >= ErrorLevel.Error);
                bool hasWarning = fileInError.Errors.Exists(error => error.Level < ErrorLevel.Error);

                if (hasError || hasWarning) {
                    // only add compilation errors
                    line.Clear();
                    line.Append("<div %ALTERNATE%style=\"background-repeat: no-repeat; background-image: url('" + (hasError ? "Error30x30" : "Warning30x30") + "'); padding-left: 40px; padding-top: 6px; padding-bottom: 6px;\">");
                    line.Append(ProExecutionCompile.FormatCompilationResultForSingleFile(fileInError.SourcePath, fileInError, null));
                    line.Append("</div>");
                    listLinesCompilation.Add(new Tuple<int, string>(hasError ? 3 : 2, line.ToString()));
                }

                if (hasError) {
                    nbCompilationError++;
                } else if (hasWarning)
                    nbCompilationWarning++;
            }
            
            // for each deploy step
            var listLinesByStep = new Dictionary<int, List<Tuple<int, string>>> {
                    {0, new List<Tuple<int, string>>()}
                };
            foreach (var kpv in _filesToDeployPerStep) {
                // group either by directory name or by pack name
                var groupDirectory = kpv.Value.GroupBy(deploy => deploy.GroupKey).Select(deploys => deploys.ToList()).ToList();

                foreach (var group in groupDirectory.OrderByDescending(list => list.First().DeployType).ThenBy(list => list.First().GroupKey)) {
                    var deployFailed = group.Exists(deploy => !deploy.IsOk);
                    var first = group.First();

                    line.Clear();
                    line.Append("<div %ALTERNATE%style=\"background-repeat: no-repeat; background-image: url('" + (deployFailed ? "Error30x30" : "Ok30x30") + "'); padding-left: 40px; padding-top: 6px; padding-bottom: 6px;\">");
                    line.Append(first.ToStringGroupHeader());
                    foreach (var fileToDeploy in group.OrderBy(deploy => deploy.To)) {
                        line.Append(fileToDeploy.ToStringDescription());
                    }
                    line.Append("</div>");

                    if (!listLinesByStep.ContainsKey(kpv.Key))
                        listLinesByStep.Add(kpv.Key, new List<Tuple<int, string>>());

                    listLinesByStep[kpv.Key].Add(new Tuple<int, string>(deployFailed ? 3 : 1, line.ToString()));

                    if (deployFailed)
                        nbDeploymentError += group.Count(deploy => !deploy.IsOk);
                    else
                        totalDeployedFiles += group.Count;
                }
            }

            // compilation
            currentReport.Append(@"<div style='padding-top: 7px; padding-bottom: 7px;'>Compiling <b>" + _proCompilation.NbFilesToCompile + "</b> files : <b>" + Utils.GetNbFilesPerType(_proCompilation.GetListOfFileToCompile.Select(compile => compile.SourcePath).ToList()).Aggregate("", (current, kpv) => current + (@"<img style='padding-right: 5px;' src='" + Utils.GetExtensionImage(kpv.Key.ToString(), true) + "' height='15px'><span style='padding-right: 12px;'>x" + kpv.Value + "</span>")) + "</b></div>");

            // compilation time
            currentReport.Append(@"<div><img style='padding-right: 20px; padding-left: 5px;' src='Time' height='15px'>Total elapsed time for the compilation : <b>" + _proCompilation.TotalCompilationTime + @"</b></div>");

            if (nbCompilationError > 0)
                currentReport.Append("<div><img style='padding-right: 20px; padding-left: 5px;' src='Error30x30' height='15px'>" + nbCompilationError + " files with compilation error(s)</div>");
            if (nbCompilationWarning > 0)
                currentReport.Append("<div><img style='padding-right: 20px; padding-left: 5px;' src='Warning30x30' height='15px'>" + nbCompilationWarning + " files with compilation warning(s)</div>");
            if (_proCompilation.NumberOfFilesTreated - nbCompilationError - nbCompilationWarning > 0)
                currentReport.Append("<div><img style='padding-right: 20px; padding-left: 5px;' src='Ok30x30' height='15px'>" + (_proCompilation.NumberOfFilesTreated - nbCompilationError - nbCompilationWarning) + " files compiled correctly</div>");

            // deploy
            currentReport.Append(@"<div style='padding-top: 7px; padding-bottom: 7px;'>Deploying <b>" + totalDeployedFiles + "</b> files : <b>" + Utils.GetNbFilesPerType(_filesToDeployPerStep.SelectMany(pair => pair.Value).Select(deploy => deploy.To).ToList()).Aggregate("", (current, kpv) => current + (@"<img style='padding-right: 5px;' src='" + Utils.GetExtensionImage(kpv.Key.ToString(), true) + "' height='15px'><span style='padding-right: 12px;'>x" + kpv.Value + "</span>")) + "</b></div>");

            // deployment time
            currentReport.Append(@"<div><img style='padding-right: 20px; padding-left: 5px;' src='Time' height='15px'>Total elapsed time for the deployment : <b>" + TotalDeploymentTime + @"</b></div>");

            if (nbDeploymentError > 0)
                currentReport.Append("<div><img style='padding-right: 20px; padding-left: 5px;' src='Error30x30' height='15px'>" + nbDeploymentError + " files not deployed</div>");
            if (totalDeployedFiles - nbDeploymentError > 0)
                currentReport.Append("<div><img style='padding-right: 20px; padding-left: 5px;' src='Ok30x30' height='15px'>" + (totalDeployedFiles - nbDeploymentError) + " files deployed correctly</div>");

            // compilation
            if (listLinesCompilation.Count > 0) {
                currentReport.Append("<h3 style='margin-top: 7px; margin-bottom: 7px;'>Compilation details :</h3>");
                var boolAlternate = false;
                foreach (var listLine in listLinesCompilation.OrderByDescending(tuple => tuple.Item1)) {
                    currentReport.Append(listLine.Item2.Replace("%ALTERNATE%", boolAlternate ? "class='AlternatBackColor' " : "class='NormalBackColor' "));
                    boolAlternate = !boolAlternate;
                }
            }

            // deployment steps
            foreach (var listLinesKpv in listLinesByStep) {
                currentReport.Append("<h3 style='margin-top: 7px; margin-bottom: 7px;'>Deployment step " + listLinesKpv.Key + " details :</h3>");
                var boolAlternate2 = false;
                foreach (var listLine in listLinesKpv.Value.OrderByDescending(tuple => tuple.Item1)) {
                    currentReport.Append(listLine.Item2.Replace("%ALTERNATE%", boolAlternate2 ? "class='AlternatBackColor' " : "class='NormalBackColor' "));
                    boolAlternate2 = !boolAlternate2;
                }
            }

            currentReport.Append("</div>");

            return currentReport.ToString();
        }

        #endregion

    }
    
}
