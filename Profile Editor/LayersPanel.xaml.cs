// Copyright 2020 Ammo Goettsch
// 
// Helios is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// Helios is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
// 

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GadrocsWorkshop.Helios.ProfileEditor.ViewModel;
using GadrocsWorkshop.Helios.Windows;
using GadrocsWorkshop.Helios.Windows.Controls;

namespace GadrocsWorkshop.Helios.ProfileEditor
{
    /// <summary>
    /// Layers panel used to sort controls in z order, hide and show, lock and unlock
    /// </summary>
    public partial class LayersPanel
    {
        private HeliosVisualContainerEditor _editor;

        /// <summary>
        /// control most recently clicked without holding shift
        /// </summary>
        private HeliosVisual _previouslyClickedControl;

        public LayersPanel()
        {
            DataContext = this;
            Focusable = false;
            InitializeComponent();
        }

        #region Properties

        public VisualsListItemCollections Controls { get; } = new VisualsListItemCollections();

        public HeliosEditorDocument Editor
        {
            get => (HeliosEditorDocument) GetValue(EditorProperty);
            set => SetValue(EditorProperty, value);
        }

        public static readonly DependencyProperty EditorProperty =
            DependencyProperty.Register("Editor", typeof(HeliosEditorDocument), typeof(LayersPanel),
                new PropertyMetadata(null, EditorChanged));


        public bool HasSelection
        {
            get => (bool) GetValue(HasSelectionProperty);
            set => SetValue(HasSelectionProperty, value);
        }

        public static readonly DependencyProperty HasSelectionProperty =
            DependencyProperty.Register("HasSelection", typeof(bool), typeof(LayersPanel), new PropertyMetadata(false));

        #endregion

        private static void EditorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            (d as LayersPanel)?.UpdateEditor();
        }

        private void UpdateEditor()
        {
            if (_editor != null)
            {
                _editor.VisualContainer.Children.CollectionChanged -= Controls_CollectionChanged;
                _editor.SelectedItems.CollectionChanged -= SelectedControls_CollectionChanged;
            }

            switch (Editor)
            {
                case PanelDocument document:
                    _editor = document.PanelEditor;
                    break;
                case MonitorDocument monitorDocument:
                    _editor = monitorDocument.MonitorEditor;
                    break;
                default:
                    _editor = null;
                    break;
            }

            if (_editor != null)
            {
                _editor.VisualContainer.Children.CollectionChanged += Controls_CollectionChanged;
                _editor.SelectedItems.CollectionChanged += SelectedControls_CollectionChanged;
            }

            CopyControlsList();
        }

        private void Controls_CollectionChanged(object sender,
            System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.BeginInvoke((Action) CopyControlsList);
        }

        // handles changes to selection that don't originate in our panel, to update the panel
        private void SelectedControls_CollectionChanged(object sender,
            System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (HeliosVisual control in e.OldItems)
                {
                    if ((e.NewItems == null || !e.NewItems.Contains(control)) && Controls.ContainsKey(control))
                    {
                        Controls[control].IsSelected = false;
                    }
                }
            }

            // ReSharper disable once InvertIf symmetry
            if (e.NewItems != null)
            {
                foreach (HeliosVisual control in e.NewItems)
                {
                    if (Controls.ContainsKey(control))
                    {
                        Controls[control].IsSelected = true;
                    }
                }
            }

            // update view model
            HasSelection = _editor.SelectedItems.Any();
        }

        private void CopyControlsList()
        {
            Controls.Clear();
            _previouslyClickedControl = null;

            if (_editor == null)
            {
                return;
            }

            // list of layers in z order is simply reverse order of immediate children of the current container
            foreach (HeliosVisual control in _editor.VisualContainer.Children.Reverse())
            {
                Controls.Add(new VisualsListItem(control, _editor.SelectedItems.Contains(control)));
            }

            // update view model
            HasSelection = _editor.SelectedItems.Any();
        }

        private void ItemLockChecked(object sender, RoutedEventArgs e)
        {
            FrameworkElement senderControl = sender as FrameworkElement;
            HeliosVisual control = (HeliosVisual) senderControl?.Tag;
            if (control != null && _editor != null && _editor.SelectedItems.Contains(control))
            {
                _editor.SelectedItems.Remove(control);
            }
        }

        // click for select, control click to multi select
        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is FrameworkElement senderControl))
            {
                // invalid message
                return;
            }

            if (!(senderControl.Tag is HeliosVisual control))
            {
                // message missing context we expect
                return;
            }

            if (_editor == null || control.IsLocked)
            {
                // invalid state for this interaction
                return;
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                int found = 0;
                foreach (VisualsListItem listItem in Controls)
                {
                    bool selectThis = (found == 1);
                    if (ReferenceEquals(control, listItem.Control))
                    {
                        // clicked item
                        found++;
                        selectThis = true;
                    }
                    else if (ReferenceEquals(_previouslyClickedControl, listItem.Control))
                    {
                        // previously clicked item
                        found++;
                        selectThis = true;
                    }

                    if (!selectThis)
                    {
                        // not in the run between the two selected controls
                        continue;
                    }

                    if (!_editor.SelectedItems.Contains(listItem.Control))
                    {
                        // should be in
                        _editor.SelectedItems.Add(listItem.Control);
                    }
                }
            }
            else 
            {
                _previouslyClickedControl = control;
                if (_editor.SelectedItems.Contains(control))
                {
                    // already selected
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                    {
                        // unselect
                        _editor.SelectedItems.Remove(control);
                    }
                    else
                    {
                        // reset selection to just this item
                        _editor.SelectedItems.Clear();
                        _editor.SelectedItems.Add(control);
                    }
                }
                else
                {
                    // not currently selected
                    if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                    {
                        // if not holding ctrl, do single select by starting over
                        _editor.SelectedItems.Clear();
                    }

                    _editor.SelectedItems.Add(control);
                }

            }

            _editor.Focus();

            // update view model
            HasSelection = _editor.SelectedItems.Any();
        }

        #region Unroutable Commands

        // NOTE: these are implemented like this so that we don't have to focus an editor to make them work

        /// <summary>
        /// backing field for property ToggleSnapControlsCommand, contains
        /// handlers for selecting snap target on/off for a set of selected controls
        /// </summary>
        private ICommand _toggleSnapControlsCommand;

        /// <summary>
        /// handlers for selecting snap target on/off for a set of selected controls
        /// </summary>
        public ICommand ToggleSnapControlsCommand
        {
            get
            {
                _toggleSnapControlsCommand = _toggleSnapControlsCommand ?? new RelayCommand(
                    parameter =>
                    {
                        Toggle(control => control.IsSnapTarget, (control, newValue) => control.IsSnapTarget = newValue);
                    },
                    paramer => AnyControlsSelected());
                return _toggleSnapControlsCommand;
            }
        }

        /// <summary>
        /// backing field for property ToggleLockControlsCommand, contains
        /// handlers for selecting locked on/off for a set of selected controls
        /// </summary>
        private ICommand _toggleLockControlsCommand;

        /// <summary>
        /// handlers for selecting locked on/off for a set of selected controls
        /// </summary>
        public ICommand ToggleLockControlsCommand
        {
            get
            {
                _toggleLockControlsCommand = _toggleLockControlsCommand ?? new RelayCommand(
                    parameter =>
                    {
                        Toggle(control => control.IsLocked, (control, newValue) => control.IsLocked = newValue);
                    },
                    paramer => AnyControlsSelected());
                return _toggleLockControlsCommand;
            }
        }

        /// <summary>
        /// backing field for property LockAllControlsCommand, contains
        /// handlers for locking all controls in the layers view regardless of selection
        /// </summary>
        private ICommand _lockAllControlsCommand;

        /// <summary>
        /// handlers for locking all controls in the layers view regardless of selection
        /// </summary>
        public ICommand LockAllControlsCommand
        {
            get
            {
                _lockAllControlsCommand = _lockAllControlsCommand ?? new RelayCommand(
                    parameter =>
                    {
                        if (Controls == null)
                        {
                            return;
                        }
                        foreach (VisualsListItem listItem in Controls)
                        {
                            // Controls collection is stable under this change, so we can make the change immediately
                            listItem.Control.IsLocked = true;
                        }
                    },
                    paramer => AnyControlsPresent());
                return _lockAllControlsCommand;
            }
        }

        /// <summary>
        /// backing field for property UnlockAllControlsCommand, contains
        /// handlers for unlocking all controls in the layers view regardless of selection
        /// </summary>
        private ICommand _unlockAllControlsCommand;

        /// <summary>
        /// handlers for locking all controls in the layers view regardless of selection
        /// </summary>
        public ICommand UnlockAllControlsCommand
        {
            get
            {
                _unlockAllControlsCommand = _unlockAllControlsCommand ?? new RelayCommand(
                    parameter =>
                    {
                        if (Controls == null)
                        {
                            return;
                        }
                        foreach (VisualsListItem listItem in Controls)
                        {
                            // Controls collection is stable under this change, so we can make the change immediately
                            listItem.Control.IsLocked = false;
                        }
                    },
                    paramer => AnyControlsPresent());
                return _unlockAllControlsCommand;
            }
        }

        /// <summary>
        /// backing field for property ToggleHideControlsCommand, contains
        /// handlers for selecting hidden on/off for a set of selected controls
        /// </summary>
        private ICommand _toggleHideControlsCommand;

        /// <summary>
        /// handlers for selecting hidden on/off for a set of selected controls
        /// </summary>
        public ICommand ToggleHideControlsCommand
        {
            get
            {
                _toggleHideControlsCommand = _toggleHideControlsCommand ?? new RelayCommand(
                    parameter =>
                    {
                        Toggle(control => control.IsHidden, (control, newValue) => control.IsHidden = newValue);
                    },
                    paramer => AnyControlsSelected());
                return _toggleHideControlsCommand;
            }
        }

        /// <summary>
        /// backing field for property MoveForwardCommand, contains
        /// handlers for moving a set of selected controls forward
        /// </summary>
        private ICommand _moveForwardCommand;

        /// <summary>
        /// handlers for moving a set of selected controls forward
        /// </summary>
        public ICommand MoveForwardCommand
        {
            get
            {
                _moveForwardCommand = _moveForwardCommand ?? new RelayCommand(
                    parameter =>
                    {
                        _editor?.MoveSelectedItemsForward();
                    },
                    paramer => AnyControlsSelected());
                return _moveForwardCommand;
            }
        }

        /// <summary>
        /// backing field for property MoveBackCommand, contains
        /// handlers for moving a set of selected controls towards the back
        /// </summary>
        private ICommand _moveBackCommand;

        /// <summary>
        /// handlers for moving a set of selected controls towards the back
        /// </summary>
        public ICommand MoveBackCommand
        {
            get
            {
                _moveBackCommand = _moveBackCommand ?? new RelayCommand(
                    parameter =>
                    {
                        _editor?.MoveSelectedItemsBack();
                    },
                    paramer => AnyControlsSelected());
                return _moveBackCommand;
            }
        }

        /// <summary>
        /// backing field for property MoveForwardFullyCommand, contains
        /// handlers for moving a set of selected controls all the way forward
        /// </summary>
        private ICommand _moveForwardFullyCommand;

        /// <summary>
        /// handlers for moving a set of selected controls all the way forward
        /// </summary>
        public ICommand MoveForwardFullyCommand
        {
            get
            {
                _moveForwardFullyCommand = _moveForwardFullyCommand ?? new RelayCommand(
                    parameter =>
                    {
                        _editor?.MoveSelectedItemsForwardFully();
                    },
                    paramer => AnyControlsSelected());
                return _moveForwardFullyCommand;
            }
        }

        /// <summary>
        /// backing field for property MoveBackFullyCommand, contains
        /// handlers for moving a set of selected controls all the way back
        /// </summary>
        private ICommand _moveBackFullyCommand;

        /// <summary>
        /// handlers for moving a set of selected controls all the way back
        /// </summary>
        public ICommand MoveBackFullyCommand
        {
            get
            {
                _moveBackFullyCommand = _moveBackFullyCommand ?? new RelayCommand(
                    parameter =>
                    {
                        _editor?.MoveSelectedItemsBackFully();
                    },
                    paramer => AnyControlsSelected());
                return _moveBackFullyCommand;
            }
        }

        /// <summary>
        /// common implementation of various Toggle... commands
        /// </summary>
        /// <param name="readerFunc">reads a boolean from each control</param>
        /// <param name="writerFunc">writes the same boolean to each control</param>
        private void Toggle(Func<HeliosVisual, bool> readerFunc,
            Func<HeliosVisual, bool, bool> writerFunc)
        {
            if (_editor?.SelectedItems == null)
            {
                return;
            }

            // first pass: determine the action we will take
            bool someSet = false;
            bool someUnset = false;
            foreach (HeliosVisual child in _editor.SelectedItems)
            {
                if (readerFunc(child))
                {
                    someSet = true;
                }
                else
                {
                    someUnset = true;
                }
            }
            bool newValue;
            if (someSet)
            {
                // clear if all were set, otherwise harmonize by setting all
                newValue = someUnset;
            }
            else
            {
                // none were set
                newValue = true;
            }

            // second pass: find items to change without changing collection directly or indirectly
            // WARNING: some property changes cause collection updates
            IList<HeliosVisual> toChange = _editor.SelectedItems.Where(control => readerFunc(control) != newValue).ToList();

            // third pass: actually make the changes
            ConfigManager.UndoManager.StartBatch();
            foreach (HeliosVisual control in toChange)
            {
                // property write ends up as Undo item
                writerFunc(control, newValue);
            }
            ConfigManager.UndoManager.CloseBatch();
        }

        private bool AnyControlsSelected()
        {
            return _editor?.SelectedItems?.Any() ?? false;
        }

        private bool AnyControlsPresent()
        {
            return Controls?.Any() ?? false;
        }

        #endregion
    }
}