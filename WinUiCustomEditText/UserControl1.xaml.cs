// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics.Display;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Text.Core;
using Windows.UI.ViewManagement;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinUiCustomEditText
{
    public sealed partial class UserControl1 : UserControl
    {
        // The _editContext lets us communicate with the input system.
        CoreTextEditContext _editContext;

        // We will use a plain text string to represent the
        // content of the custom text edit control.
        string _text = String.Empty;

        // If the _selection starts and ends at the same point,
        // then it represents the location of the caret (insertion point).
        CoreTextRange _selection;

        // _internalFocus keeps track of whether our control acts like it has focus.
        bool _internalFocus = false;

        // If there is a nonempty selection, then _extendingLeft is true if the user
        // is using shift+arrow to adjust the starting point of the selection,
        // or false if the user is adjusting the ending point of the selection.
        bool _extendingLeft = false;

        public UserControl1()
        {
            this.InitializeComponent();
            BorderPanel.KeyDown += BorderPanel_KeyDown;
            BorderPanel.PointerPressed += BorderPanel_PointerPressed;
            CoreTextServicesManager manager = CoreTextServicesManager.GetForCurrentView();
            _editContext = manager.CreateEditContext();
            
            _editContext.InputPaneDisplayPolicy = CoreTextInputPaneDisplayPolicy.Manual;

            // Set the input scope to Text because this text box is for any text.
            // This also informs software keyboards to show their regular
            // text entry layout.  There are many other input scopes and each will
            // inform a keyboard layout and text behavior.
            _editContext.InputScope = CoreTextInputScope.Text;

            // The system raises this event to request a specific range of text.
            _editContext.TextRequested += EditContext_TextRequested;

            // The system raises this event to request the current selection.
            _editContext.SelectionRequested += EditContext_SelectionRequested;

            // The system raises this event when it wants the edit control to remove focus.
            _editContext.FocusRemoved += EditContext_FocusRemoved;

            // The system raises this event to update text in the edit control.
            _editContext.TextUpdating += EditContext_TextUpdating;

            // The system raises this event to change the selection in the edit control.
            _editContext.SelectionUpdating += EditContext_SelectionUpdating;

            // The system raises this event when it wants the edit control
            // to apply formatting on a range of text.
            _editContext.FormatUpdating += EditContext_FormatUpdating;

            // The system raises this event to request layout information.
            // This is used to help choose a position for the IME candidate window.
            _editContext.LayoutRequested += EditContext_LayoutRequested;

            // The system raises this event to notify the edit control
            // that the string composition has started.
            _editContext.CompositionStarted += EditContext_CompositionStarted;

            // The system raises this event to notify the edit control
            // that the string composition is finished.
            _editContext.CompositionCompleted += EditContext_CompositionCompleted;

            // The system raises this event when the NotifyFocusLeave operation has
            // completed. Our sample does not use this event.
            // _editContext.NotifyFocusLeaveCompleted += EditContext_NotifyFocusLeaveCompleted;

            // Set our initial UI.
            UpdateTextUI();
            UpdateFocusUI();
        }

        private void BorderPanel_KeyDown(object sender, KeyRoutedEventArgs args)
        {
            // have focus.
            if (!_internalFocus)
            {
                return;
            }

            // This holds the range we intend to operate on, or which we intend
            // to become the new selection. Start with the current selection.
            CoreTextRange range = _selection;

            // For the purpose of this sample, we will support only the left and right
            // arrow keys and the backspace key. A more complete text edit control
            // would also handle keys like Home, End, and Delete, as well as
            // hotkeys like Ctrl+V to paste.
            //
            // Note that this sample does not properly handle surrogate pairs
            // nor does it handle grapheme clusters.
            
            switch (args.Key)
            {
                // Backspace
                case VirtualKey.Back:
                    // If there is a selection, then delete the selection.
                    if (HasSelection())
                    {
                        // Set the text in the selection to nothing.
                        ReplaceText(range, "");
                    }
                    else
                    {
                        // Delete the character to the left of the caret, if one exists,
                        // by creating a range that encloses the character to the left
                        // of the caret, and setting the contents of that range to nothing.
                        range.StartCaretPosition = Math.Max(0, range.StartCaretPosition - 1);
                        ReplaceText(range, "");
                    }
                    break;

                // Left arrow
                case VirtualKey.Left:
                    range.StartCaretPosition = Math.Max(0, range.StartCaretPosition - 1);
                    range.EndCaretPosition = range.StartCaretPosition;
                    SetSelectionAndNotify(range);
                    break;

                // Right arrow
                case VirtualKey.Right:
                    range.StartCaretPosition = Math.Min(_text.Length, range.StartCaretPosition + 1);
                    range.EndCaretPosition = range.StartCaretPosition;
                    SetSelectionAndNotify(range);
                    break;
            }
        }


        #region Focus management
        void BorderPanel_PointerPressed(object sender, PointerRoutedEventArgs args)
        {
            // See whether the pointer is inside or outside the control.
            var pos = args.GetCurrentPoint(BorderPanel).Position;
            Rect contentRect = GetElementRect(BorderPanel);
            if (contentRect.Contains(pos))
            {
                // The user tapped inside the control. Give it focus.
                SetInternalFocus();

                // Tell XAML that this element has focus, so we don't have two
                // focus elements. That is the extent of our integration with XAML focus.
                Focus(FocusState.Programmatic);

                // A more complete custom control would move the caret to the
                // pointer position. It would also provide some way to select
                // text via touch. We do neither in this sample.

            }
            else
            {
                // The user tapped outside the control. Remove focus.
                RemoveInternalFocus();
            }
        }

        void SetInternalFocus()
        {
            if (!_internalFocus)
            {
                // Update internal notion of focus.
                _internalFocus = true;

                // Update the UI.
                UpdateTextUI();
                UpdateFocusUI();

                // Notify the CoreTextEditContext that the edit context has focus,
                // so it should start processing text input.
                _editContext.NotifyFocusEnter();
            }

        }

        void RemoveInternalFocus()
        {
            if (_internalFocus)
            {
                //Notify the system that this edit context is no longer in focus
                _editContext.NotifyFocusLeave();

                RemoveInternalFocusWorker();
            }
        }

        void RemoveInternalFocusWorker()
        {
            //Update the internal notion of focus
            _internalFocus = false;

            // Update our UI.
            UpdateTextUI();
            UpdateFocusUI();
        }

        void EditContext_FocusRemoved(CoreTextEditContext sender, object args)
        {
            RemoveInternalFocusWorker();
        }

        void Element_Unloaded(object sender, RoutedEventArgs e)
        {
            BorderPanel.KeyDown -= BorderPanel_KeyDown;
        }
        #endregion

        #region Text management
        // Replace the text in the specified range.
        void ReplaceText(CoreTextRange modifiedRange, string text)
        {
            // Modify the internal text store.
            _text = _text.Substring(0, modifiedRange.StartCaretPosition) +
              text +
              _text.Substring(modifiedRange.EndCaretPosition);

            // Move the caret to the end of the replacement text.
            _selection.StartCaretPosition = modifiedRange.StartCaretPosition + text.Length;
            _selection.EndCaretPosition = _selection.StartCaretPosition;

            // Update the selection of the edit context.  There is no need to notify the system
            // of the selection change because we are going to call NotifyTextChanged soon.
            SetSelection(_selection);

            // Let the CoreTextEditContext know what changed.
            _editContext.NotifyTextChanged(modifiedRange, text.Length, _selection);
        }

        bool HasSelection()
        {
            return _selection.StartCaretPosition != _selection.EndCaretPosition;
        }

        // Change the selection without notifying CoreTextEditContext of the new selection.
        void SetSelection(CoreTextRange selection)
        {
            // Modify the internal selection.
            _selection = selection;

            //Update the UI to show the new selection.
            UpdateTextUI();
        }

        // Change the selection and notify CoreTextEditContext of the new selection.
        void SetSelectionAndNotify(CoreTextRange selection)
        {
            SetSelection(selection);
            _editContext.NotifySelectionChanged(_selection);
        }

        // Return the specified range of text. Note that the system may ask for more text
        // than exists in the text buffer.
        void EditContext_TextRequested(CoreTextEditContext sender, CoreTextTextRequestedEventArgs args)
        {
            CoreTextTextRequest request = args.Request;
            request.Text = _text.Substring(
                request.Range.StartCaretPosition,
                Math.Min(request.Range.EndCaretPosition, _text.Length) - request.Range.StartCaretPosition);
        }

        // Return the current selection.
        void EditContext_SelectionRequested(CoreTextEditContext sender, CoreTextSelectionRequestedEventArgs args)
        {
            CoreTextSelectionRequest request = args.Request;
            request.Selection = _selection;
        }

        void EditContext_TextUpdating(CoreTextEditContext sender, CoreTextTextUpdatingEventArgs args)
        {
            ////////// text updating
            CoreTextRange range = args.Range;
            string newText = args.Text;
            CoreTextRange newSelection = args.NewSelection;

            // Modify the internal text store.
            _text = _text.Substring(0, range.StartCaretPosition) +
                newText +
                _text.Substring(Math.Min(_text.Length, range.EndCaretPosition));

            // You can set the proper font or direction for the updated text based on the language by checking
            // args.InputLanguage.  We will not do that in this sample.

            // Modify the current selection.
            newSelection.EndCaretPosition = newSelection.StartCaretPosition;

            // Update the selection of the edit context. There is no need to notify the system
            // because the system itself changed the selection.
            SetSelection(newSelection);
        }

        void EditContext_SelectionUpdating(CoreTextEditContext sender, CoreTextSelectionUpdatingEventArgs args)
        {
            // Set the new selection to the value specified by the system.
            CoreTextRange range = args.Selection;

            // Update the selection of the edit context. There is no need to notify the system
            // because the system itself changed the selection.
            SetSelection(range);
        }
        #endregion

        #region Formatting and layout
        void EditContext_FormatUpdating(CoreTextEditContext sender, CoreTextFormatUpdatingEventArgs args)
        {
            // The following code specifies how you would apply any formatting to the specified range of text
            // For this sample, we do not make any changes to the format.

            // Apply text color if specified.
            // A null value indicates that the default should be used.
            if (args.TextColor != null)
            {
                //InternalSetTextColor(args.Range, args.TextColor.Value);
            }
            else
            {
                //InternalSetDefaultTextColor(args.Range);
            }

            // Apply background color if specified.
            // A null value indicates that the default should be used.
            if (args.BackgroundColor != null)
            {
                //InternalSetBackgroundColor(args.Range, args.BackgroundColor.Value);
            }
            else
            {
                //InternalSetDefaultBackgroundColor(args.Range);
            }

            // Apply underline if specified.
            // A null value indicates that the default should be used.
            if (args.UnderlineType != null)
            {
                //TextDecoration underline = new TextDecoration(args.Range,args.UnderlineType.Value,args.UnderlineColor.Value);

                //InternalAddTextDecoration(underline);
            }
            else
            {
                //InternalRemoveTextDecoration(args.Range);
            }
        }

        static Rect ScaleRect(Rect rect, double scale)
        {
            rect.X *= scale;
            rect.Y *= scale;
            rect.Width *= scale;
            rect.Height *= scale;
            return rect;
        }

        void EditContext_LayoutRequested(CoreTextEditContext sender, CoreTextLayoutRequestedEventArgs args)
        {
            CoreTextLayoutRequest request = args.Request;

            // Get the screen coordinates of the entire control and the selected text.
            // This information is used to position the IME candidate window.

            // First, get the coordinates of the edit control and the selection
            // relative to the Window.
            Rect contentRect = GetElementRect(ContentPanel);
            Rect selectionRect = GetElementRect(SelectionText);

            // Next, convert to screen coordinates in view pixels.
            Rect windowBounds = new Rect(200, 200, 200, 200);
            contentRect.X += windowBounds.X;
            contentRect.Y += windowBounds.Y;
            selectionRect.X += windowBounds.X;
            selectionRect.Y += windowBounds.Y;

            // Finally, scale up to raw pixels.
            double scaleFactor = 2;

            contentRect = ScaleRect(contentRect, scaleFactor);
            selectionRect = ScaleRect(selectionRect, scaleFactor);

            // This is the bounds of the selection.
            // Note: If you return bounds with 0 width and 0 height, candidates will not appear while typing.
            request.LayoutBounds.TextBounds = selectionRect;

            //This is the bounds of the whole control
            request.LayoutBounds.ControlBounds = contentRect;
        }
        #endregion

        #region Input
        // This indicates that an IME has started composition.  If there is no handler for this event,
        // then composition will not start.
        void EditContext_CompositionStarted(CoreTextEditContext sender, CoreTextCompositionStartedEventArgs args)
        {
        }

        void EditContext_CompositionCompleted(CoreTextEditContext sender, CoreTextCompositionCompletedEventArgs args)
        {
        }

        void CoreWindow_KeyDown(CoreWindow sender, KeyEventArgs args)
        {
            // Do not process keyboard input if the custom edit control does not
            // have focus.
            if (!_internalFocus)
            {
                return;
            }

            // This holds the range we intend to operate on, or which we intend
            // to become the new selection. Start with the current selection.
            CoreTextRange range = _selection;

            // For the purpose of this sample, we will support only the left and right
            // arrow keys and the backspace key. A more complete text edit control
            // would also handle keys like Home, End, and Delete, as well as
            // hotkeys like Ctrl+V to paste.
            //
            // Note that this sample does not properly handle surrogate pairs
            // nor does it handle grapheme clusters.

            switch (args.VirtualKey)
            {
                // Backspace
                case VirtualKey.Back:
                    // If there is a selection, then delete the selection.
                    if (HasSelection())
                    {
                        // Set the text in the selection to nothing.
                        ReplaceText(range, "");
                    }
                    else
                    {
                        // Delete the character to the left of the caret, if one exists,
                        // by creating a range that encloses the character to the left
                        // of the caret, and setting the contents of that range to nothing.
                        range.StartCaretPosition = Math.Max(0, range.StartCaretPosition - 1);
                        ReplaceText(range, "");
                    }
                    break;

                // Left arrow
                case VirtualKey.Left:
                    range.EndCaretPosition = range.StartCaretPosition;
                    SetSelectionAndNotify(range);
                    break;

                // Right arrow
                case VirtualKey.Right:
                    range.StartCaretPosition = Math.Min(_text.Length, range.StartCaretPosition + 1);
                    range.EndCaretPosition = range.StartCaretPosition;
                    SetSelectionAndNotify(range);
                    break;
            }
        }

        // Adjust the active endpoint of the selection in the specified direction.
        void AdjustSelectionEndpoint(int direction)
        {
            CoreTextRange range = _selection;
            if (_extendingLeft)
            {
                range.StartCaretPosition = Math.Max(0, range.StartCaretPosition + direction);
            }
            else
            {
                range.EndCaretPosition = Math.Min(_text.Length, range.EndCaretPosition + direction);
            }

            SetSelectionAndNotify(range);
        }
        #endregion

        #region UI
        // Helper function to put a zero-width non-breaking space at the end of a string.
        // This prevents TextBlock from trimming trailing spaces.
        static string PreserveTrailingSpaces(string s)
        {
            return s + "\ufeff";
        }

        void UpdateFocusUI()
        {
            BorderPanel.BorderBrush = _internalFocus ? new SolidColorBrush(Microsoft.UI.Colors.Green) : null;
        }

        void UpdateTextUI()
        {
            // The raw materials we have are a string (_text) and information about
            // where the caret/selection is (_selection). We can render the control
            // any way we like.

            BeforeSelectionText.Text = PreserveTrailingSpaces(_text.Substring(0, _selection.StartCaretPosition));
            if (HasSelection())
            {
                // There is a selection. Draw that.
                CaretText.Visibility = Visibility.Collapsed;
                SelectionText.Text = PreserveTrailingSpaces(
                    _text.Substring(_selection.StartCaretPosition, _selection.EndCaretPosition - _selection.StartCaretPosition));
            }
            else
            {
                // There is no selection. Remove it.
                SelectionText.Text = "";

                // Draw the caret if we have focus.
                CaretText.Visibility = _internalFocus ? Visibility.Visible : Visibility.Collapsed;
            }

            AfterSelectionText.Text = PreserveTrailingSpaces(_text.Substring(_selection.EndCaretPosition));

            // Update statistics for demonstration purposes.
            FullText.Text = _text;
            SelectionStartIndexText.Text = _selection.StartCaretPosition.ToString();
            SelectionEndIndexText.Text = _selection.EndCaretPosition.ToString();
        }

        static Rect GetElementRect(FrameworkElement element)
        {
            GeneralTransform transform = element.TransformToVisual(null);
            Point point = transform.TransformPoint(new Point());
            return new Rect(point, new Size(element.ActualWidth, element.ActualHeight));
        }
        #endregion
    }
}
