using System;
using System.Windows.Input;

namespace ElyProxy.ViewModels;

public class RelayCommand : ICommand
{
	private readonly Action<object?> _execute;

	private readonly Func<object?, bool>? _canExecute;

	public event EventHandler? CanExecuteChanged
	{
		add
		{
			CommandManager.RequerySuggested += value;
		}
		remove
		{
			CommandManager.RequerySuggested -= value;
		}
	}

	public RelayCommand(Action execute, Func<bool>? canExecute = null)
		: this(delegate
		{
			execute();
		}, (canExecute != null) ? ((Func<object, bool>)((object? _) => canExecute())) : null)
	{
	}

	public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
	{
		_execute = execute ?? throw new ArgumentNullException("execute");
		_canExecute = canExecute;
	}

	public bool CanExecute(object? parameter)
	{
		return _canExecute?.Invoke(parameter) ?? true;
	}

	public void Execute(object? parameter)
	{
		if (CanExecute(parameter)) _execute(parameter);
	}
}
