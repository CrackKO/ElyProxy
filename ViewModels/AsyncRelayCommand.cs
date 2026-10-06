using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ElyProxy.ViewModels;

public class AsyncRelayCommand : ICommand
{
	private readonly Func<Task> _execute;
	private readonly Func<bool>? _canExecute;
	private bool _isExecuting;

	public event EventHandler? CanExecuteChanged
	{
		add => CommandManager.RequerySuggested += value;
		remove => CommandManager.RequerySuggested -= value;
	}

	public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
	{
		_execute = execute ?? throw new ArgumentNullException("execute");
		_canExecute = canExecute;
	}

	public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

	public async void Execute(object? parameter)
	{
		if (_isExecuting)
			return;

		_isExecuting = true;
		CommandManager.InvalidateRequerySuggested();
		try
		{
			await _execute();
		}
		finally
		{
			_isExecuting = false;
			CommandManager.InvalidateRequerySuggested();
		}
	}
}
