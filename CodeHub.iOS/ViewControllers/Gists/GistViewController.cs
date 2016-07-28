using System;
using CodeHub.Core.ViewModels.Gists;
using GitHubSharp.Models;
using UIKit;
using CodeHub.Utilities;
using System.Linq;
using System.Threading.Tasks;
using CodeHub.DialogElements;
using CodeHub.Core.Services;
using System.Collections.Generic;
using CodeHub.Services;
using System.Reactive.Linq;
using ReactiveUI;
using Splat;

namespace CodeHub.ViewControllers.Gists
{
    public class GistViewController : PrettyDialogViewController<GistViewModel>
    {
        private SplitViewElement _splitRow1, _splitRow2;
        private StringElement _ownerElement;
        private SplitButtonElement _split;
        private readonly IAlertDialogService _alertDialogService = Locator.Current.GetService<IAlertDialogService>();

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            var editButton = NavigationItem.RightBarButtonItem = new UIBarButtonItem(UIBarButtonSystemItem.Action);

            HeaderView.SetImage(null, Images.Avatar);
            HeaderView.Text = "Gist #" + ViewModel.Id;
            HeaderView.SubImageView.TintColor = UIColor.FromRGB(243, 156, 18);

            Appeared.Take(1)
                .Select(_ => Observable.Timer(TimeSpan.FromSeconds(0.35f)).Take(1))
                .Switch()
                .Select(_ => ViewModel.WhenAnyValue(x => x.IsStarred))
                .Switch()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(x => HeaderView.SetSubImage(x ? Octicon.Star.ToImage() : null));

            TableView.RowHeight = UITableView.AutomaticDimension;
            TableView.EstimatedRowHeight = 44f;

            _split = new SplitButtonElement();
            var files = _split.AddButton("Files", "-");
            var comments = _split.AddButton("Comments", "-");
            var forks = _split.AddButton("Forks", "-");

            _splitRow1 = new SplitViewElement(Octicon.Lock.ToImage(), Octicon.Package.ToImage());
            _splitRow2 = new SplitViewElement(Octicon.Calendar.ToImage(), Octicon.Star.ToImage());
            _ownerElement = new StringElement("Owner", string.Empty, UITableViewCellStyle.Value1) { 
                Image = Octicon.Person.ToImage(),
                Accessory = UITableViewCellAccessory.DisclosureIndicator
            };

            OnActivation(d =>
            {
                editButton.GetClickedObservable().Subscribe(_ => ShareButtonTap(editButton)).AddTo(d);
                _ownerElement.Clicked.InvokeCommand(ViewModel.GoToUserCommand).AddTo(d);

                ViewModel.WhenAnyValue(x => x.IsStarred)
                         .Subscribe(isStarred => _splitRow2.Button2.Text = isStarred ? "Starred" : "Not Starred")
                         .AddTo(d);

                ViewModel.BindCollection(x => x.Comments, true)
                         .Subscribe(_ => RenderGist())
                         .AddTo(d);
                
                HeaderView.Clicked
                          .InvokeCommand(ViewModel.GoToUserCommand)
                          .AddTo(d);

                ViewModel.WhenAnyValue(x => x.Gist).Where(x => x != null).Subscribe(gist =>
                {
                    _splitRow1.Button1.Text = (gist.Public ?? true) ? "Public" : "Private";
                    _splitRow1.Button2.Text = (gist.History?.Count ?? 0) + " Revisions";
                    _splitRow2.Button1.Text = gist.CreatedAt.Day + " Days Old";
                    _ownerElement.Value = gist.Owner?.Login ?? "Unknown";
                    files.Text = gist.Files.Count.ToString();
                    comments.Text = gist.Comments.ToString();
                    forks.Text = gist.Forks?.Count.ToString() ?? "-";
                    HeaderView.SubText = gist.Description;
                    HeaderView.Text = gist.Files?.Select(x => x.Key).FirstOrDefault() ?? HeaderView.Text;
                    HeaderView.SetImage(gist.Owner?.AvatarUrl, Images.Avatar);
                    RenderGist();
                    RefreshHeaderView();
                }).AddTo(d);
            });
        }

        public void RenderGist()
        {
            if (ViewModel.Gist == null) return;
            var model = ViewModel.Gist;

            ICollection<Section> sections = new LinkedList<Section>();
            sections.Add(new Section { _split });
            sections.Add(new Section { _splitRow1, _splitRow2, _ownerElement });
            var sec2 = new Section();
            sections.Add(sec2);

            var weakVm = new WeakReference<GistViewModel>(ViewModel);
            foreach (var file in model.Files.Keys)
            {
                var sse = new ButtonElement(file, Octicon.FileCode.ToImage())
                { 
                    LineBreakMode = UILineBreakMode.TailTruncation,
                };

                var fileSaved = file;
                var gistFileModel = model.Files[fileSaved];
                sse.Clicked.Subscribe(MakeCallback(weakVm, gistFileModel));
                sec2.Add(sse);
            }

            if (ViewModel.Comments.Items.Count > 0)
            {
                var sec3 = new Section("Comments");
                sec3.AddAll(ViewModel.Comments.Select(x => new CommentElement(x.User?.Login ?? "Anonymous", x.Body, x.CreatedAt, x.User?.AvatarUrl)));
                sections.Add(sec3);
            }

            Root.Reset(sections);
        }

        private static Action<object> MakeCallback(WeakReference<GistViewModel> weakVm, GistFileModel model)
        {
            return new Action<object>(_ => weakVm.Get()?.GoToFileSourceCommand.Execute(model));
        }

        private async Task Fork()
        {
            try
            {
                await this.DoWorkAsync("Forking...", () => ViewModel.ForkCommand.ExecuteAsyncTask());
            }
            catch (Exception ex)
            {
                _alertDialogService.Alert("Error", ex.Message).ToBackground();
            }
        }

        private async Task Compose()
        {
            try
            {
                var app = Locator.Current.GetService<IApplicationService>();
                var data = await this.DoWorkAsync("Loading...", () => app.Client.ExecuteAsync(app.Client.Gists[ViewModel.Id].Get()));
                var gistController = new EditGistController(data.Data);
                gistController.Created = editedGist => ViewModel.Gist = editedGist;
                var navController = new UINavigationController(gistController);
                PresentViewController(navController, true, null);
            }
            catch (Exception ex)
            {
                _alertDialogService.Alert("Error", ex.Message).ToBackground();
            }
        }

        void ShareButtonTap (object sender)
        {
            if (ViewModel.Gist == null)
                return;

            var app = Locator.Current.GetService<IApplicationService>();
            var isOwner = string.Equals(app.Account.Username, ViewModel.Gist?.Owner?.Login, StringComparison.OrdinalIgnoreCase);

            var sheet = new UIActionSheet();
            var editButton = sheet.AddButton(isOwner ? "Edit" : "Fork");
            var starButton = sheet.AddButton(ViewModel.IsStarred ? "Unstar" : "Star");
            var shareButton = sheet.AddButton("Share");
            var showButton = sheet.AddButton("Show in GitHub");
            var cancelButton = sheet.AddButton("Cancel");
            sheet.CancelButtonIndex = cancelButton;
            sheet.DismissWithClickedButtonIndex(cancelButton, true);
            sheet.Dismissed += (s, e) =>
            {
                BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        if (e.ButtonIndex == shareButton)
                            AlertDialogService.ShareUrl(ViewModel.Gist?.HtmlUrl, sender as UIBarButtonItem);
                        else if (e.ButtonIndex == showButton)
                            ViewModel.GoToHtmlUrlCommand.Execute(null);
                        else if (e.ButtonIndex == starButton)
                            ViewModel.ToggleStarCommand.Execute(null);
                        else if (e.ButtonIndex == editButton)
                            Compose().ToBackground();
                    }
                    catch
                    {
                    }
                });

                sheet.Dispose();
            };

            sheet.ShowFromToolbar(NavigationController.Toolbar);
        }
    }
}
