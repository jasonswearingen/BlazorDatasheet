using BlazorDatasheet.Edit;
using BlazorDatasheet.Interfaces;
using BlazorDatasheet.Model;
using BlazorDatasheet.Render;
using BlazorDatasheet.Services;
using BlazorDatasheet.Util;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDatasheet;

public partial class Datasheet : IHandleEvent
{
    [Parameter] public Sheet? Sheet { get; set; }
    [Parameter] public bool IsReadOnly { get; set; }
    [Parameter] public EventCallback<CellChangedEventArgs> OnCellChanged { get; set; }
    [Parameter] public double FixedHeightInPx { get; set; } = 350;
    [Parameter] public bool IsFixedHeight { get; set; }
    public EditorManager EditorManager { get; private set; }
    private bool IsDataSheetActive { get; set; }
    private bool IsMouseInsideSheet { get; set; }
    private ElementReference ActiveCellInputReference;
    private Queue<Action> QueuedActions { get; set; } = new Queue<Action>();

    private IWindowEventService _windowEventService;

    protected override void OnInitialized()
    {
        _windowEventService = new WindowEventService(JS);
        EditorManager = new EditorManager(this.Sheet, NextTick);
        EditorManager.OnAcceptEdit += EditorManagerOnOnAcceptEdit;

        base.OnInitialized();
    }

    private void EditorManagerOnOnAcceptEdit(AcceptEditEventArgs e)
    {
        var cell = Sheet.GetCell(e.Row, e.Col);
        this.emitCellChanged(cell, e.Row, e.Col);
    }

    private Type getCellRendererType(string type)
    {
        if (Sheet?.RenderComponentTypes.ContainsKey(type) == true)
            return Sheet.RenderComponentTypes[type];

        return typeof(TextRenderer);
    }

    private Dictionary<string, object> getCellRendererParameters(Cell cell, int row, int col)
    {
        return new Dictionary<string, object>()
        {
            { "Cell", cell },
            { "Row", row },
            { "Col", col },
            { "OnChangeCellValueRequest", HandleCellRendererRequestChangeValue },
            { "OnBeginEditRequest", HandleCellRequestBeginEdit }
        };
    }

    private Dictionary<string, object> getEditorParameters()
    {
        return new Dictionary<string, object>()
        {
            { "EditorManager", EditorManager },
        };
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await AddWindowEventsAsync();
        }

        while (QueuedActions.Any())
        {
            var action = QueuedActions.Dequeue();
            action.Invoke();
        }
    }

    private async Task AddWindowEventsAsync()
    {
        await _windowEventService.Init();
        _windowEventService.OnKeyDown += HandleWindowKeyDown;
        _windowEventService.OnMouseDown += HandleWindowMouseDown;
    }

    private void HandleCellMouseUp(int row, int col, MouseEventArgs e)
    {
        if (Sheet.IsSelecting)
        {
            Sheet?.EndSelecting();
            StateHasChanged();
        }
    }

    private void HandleCellMouseDown(int row, int col, MouseEventArgs e)
    {
        if (EditorManager.IsEditing && !EditorManager.CurrentEditPosition.Equals(row, col))
        {
            AcceptEdit();
        }


        if (e.ShiftKey)
            Sheet?.ExtendSelection(row, col);
        else
            Sheet?.BeginSelecting(row, col, !e.MetaKey, SelectionMode.Cell);

        StateHasChanged();
    }

    private void HandleColumnHeaderMouseDown(int col, MouseEventArgs e)
    {
        AcceptEdit();

        if (e.ShiftKey)
            Sheet?.ExtendSelection(Sheet.NumRows, col);
        else
        {
            Sheet?.BeginSelecting(0, col, !e.MetaKey, SelectionMode.Column);
            Sheet?.UpdateSelectingEndPosition(Sheet.NumRows, col);
        }

        StateHasChanged();
    }

    private void HandleRowHeaderMouseDown(int row, MouseEventArgs e)
    {
        AcceptEdit();

        if (e.ShiftKey)
            Sheet?.ExtendSelection(row, Sheet.NumCols);
        else
        {
            Sheet?.BeginSelecting(row, 0, !e.MetaKey, SelectionMode.Row);
            Sheet?.UpdateSelectingEndPosition(row, Sheet.NumCols);
        }

        StateHasChanged();
    }

    private void HandleCellDoubleClick(int row, int col, MouseEventArgs e)
    {
        BeginEdit(row, col, softEdit: false, EditEntryMode.Mouse);
        StateHasChanged();
    }

    private void BeginEdit(int row, int col, bool softEdit, EditEntryMode mode, string entryChar = "")
    {
        if (this.IsReadOnly)
            return;

        this.Sheet.CancelSelecting();

        var cell = Sheet?.GetCell(row, col);
        if (cell == null || cell.IsReadOnly)
            return;

        EditorManager.BeginEdit(row, col, cell, softEdit, mode, entryChar);

        // Required to re-render after any edit component reference has changed
        NextTick(StateHasChanged);
    }

    private bool AcceptEdit()
    {
        return AcceptEdit(0, 0);
    }

    /// <summary>
    /// Accepts the current edit and moves the selection by dRow/dCol, returning whether the edit was successful
    /// </summary>
    /// <param name="dRow"></param>
    /// <param name="dCol"></param>
    /// <returns></returns>
    private bool AcceptEdit(int dRow, int dCol)
    {
        var result = EditorManager.AcceptEdit();
        if (!result)
            return false;

        Sheet.MoveSelection(dRow, dCol);
        StateHasChanged();

        return result;
    }

    private async void emitCellChanged(Cell cell, int row, int col)
    {
        if (!OnCellChanged.HasDelegate)
            return;
        await OnCellChanged.InvokeAsync(new CellChangedEventArgs(cell, row, col));
    }

    /// <summary>
    /// Cancels the current edit, returning whether the edit was successful
    /// </summary>
    /// <returns></returns>
    private bool CancelEdit()
    {
        var result = EditorManager.CancelEdit();
        if (result)
            StateHasChanged();

        return result;
    }

    private void HandleCellMouseOver(int row, int col, MouseEventArgs e)
    {
        if (Sheet?.IsSelecting == true)
        {
            if (Sheet.SelectionMode == SelectionMode.Cell)
                Sheet.UpdateSelectingEndPosition(row, col);
            else if (Sheet.SelectionMode == SelectionMode.Column)
                Sheet.UpdateSelectingEndPosition(Sheet.NumRows, col);
            else if (Sheet.SelectionMode == SelectionMode.Row)
                Sheet.UpdateSelectingEndPosition(row, Sheet.NumCols);
            StateHasChanged();
        }
    }

    private bool HandleWindowMouseDown(MouseEventArgs e)
    {
        bool changed = IsDataSheetActive != IsMouseInsideSheet;
        IsDataSheetActive = IsMouseInsideSheet;

        if (!IsDataSheetActive && AcceptEdit()) // if it is outside
        {
            changed = true;
        }

        if (changed)
            StateHasChanged();

        return false;
    }

    private bool HandleWindowKeyDown(KeyboardEventArgs e)
    {
        if (!IsDataSheetActive)
            return false;

        var editorHandled = EditorManager.HandleKeyDown(e.Key, e.CtrlKey, e.ShiftKey, e.AltKey, e.MetaKey);
        if (editorHandled)
            return true;

        if (e.Key == "Escape")
        {
            return CancelEdit();
        }

        if (e.Key == "Enter")
        {
            if (!EditorManager.IsEditing)
            {
                Sheet?.EndSelecting();
                Sheet?.MoveSelection(1, 0);
                StateHasChanged();
                return true;
            }

            // Accept the edit
            else if (AcceptEdit(1, 0))
            {
                return true;
            }
        }

        if (KeyUtil.IsArrowKey(e.Key))
        {
            var direction = KeyUtil.GetKeyMovementDirection(e.Key);
            if (!EditorManager.IsEditing)
            {
                Sheet?.MoveSelection(direction.Item1, direction.Item2);
                StateHasChanged();
                return true;
            }
            // Accept the edit
            else if (EditorManager.IsSoftEdit && AcceptEdit(direction.Item1, direction.Item2))
            {
                return true;
            }
        }

        if (e.Key == "Tab")
        {
            AcceptEdit();
            Sheet.MoveSelection(0, 1);
            StateHasChanged();
            return true;
        }

        if ((e.Key.Length == 1) && !EditorManager.IsEditing && IsDataSheetActive)
        {
            char c = e.Key == "Space" ? ' ' : e.Key[0];
            if (char.IsLetterOrDigit(c) || char.IsPunctuation(c) || char.IsSymbol(c) || char.IsSeparator(c))
            {
                var inputPosition = Sheet?.GetInputForSelection();
                if (inputPosition == null)
                    return false;
                BeginEdit(inputPosition.Row, inputPosition.Col, softEdit: true, EditEntryMode.Key, e.Key);
                StateHasChanged();
            }

            return true;
        }

        return false;
    }

    private void NextTick(Action action)
    {
        QueuedActions.Enqueue(action);
    }

    Task IHandleEvent.HandleEventAsync(
        EventCallbackWorkItem callback, object? arg) => callback.InvokeAsync(arg);

    public void Dispose()
    {
        _windowEventService.Dispose();
    }

    /// <summary>
    /// Handles when a cell renderer requests to start editing the cell
    /// </summary>
    /// <param name="args"></param>
    private void HandleCellRequestBeginEdit(EditRequestArgs args)
    {
        BeginEdit(args.Row, args.Col, args.IsSoftEdit, args.EntryMode);
        StateHasChanged();
    }

    /// <summary>
    /// Handles when a cell renderer requests that a cell's value be changed
    /// </summary>
    /// <param name="args"></param>
    private void HandleCellRendererRequestChangeValue(ChangeCellRequestEventArgs args)
    {
        var cell = Sheet?.GetCell(args.Row, args.Col);
        var setValue = cell.SetValue(args.NewValue);
        if (!setValue)
            return;
        StateHasChanged();
        emitCellChanged(cell, args.Row, args.Col);
    }
}