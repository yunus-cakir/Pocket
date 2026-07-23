using CommunityToolkit.Mvvm.Input;
using Pocket.Client.Models;

namespace Pocket.Client.PageModels
{
    public interface IProjectTaskPageModel
    {
        IAsyncRelayCommand<ProjectTask> NavigateToTaskCommand { get; }
        bool IsBusy { get; }
    }
}