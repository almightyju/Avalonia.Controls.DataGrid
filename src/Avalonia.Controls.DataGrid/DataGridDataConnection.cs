﻿// (c) Copyright Microsoft Corporation.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

#nullable disable

using Avalonia.Collections;
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.ComponentModel.DataAnnotations;
using Avalonia.Utilities;
using Avalonia.Controls.Utils;

namespace Avalonia.Controls
{
    internal class DataGridDataConnection
    {

        private int _backupSlotForCurrentChanged;
        private int _columnForCurrentChanged;
        private PropertyInfo[] _dataProperties;
        private IEnumerable _dataSource;
        private Type _dataType;
        private bool _expectingCurrentChanged;
        private object _itemToSelectOnCurrentChanged;
        private DataGrid _owner;
        private bool _scrollForCurrentChanged;
        private DataGridSelectionAction _selectionActionForCurrentChanged;
        private bool _onCollectionChangeAddRemoveNewRowPlaceholder;

        public DataGridDataConnection(DataGrid owner)
        {
            _owner = owner;
        }

        public bool AllowEdit
        {
            get
            {
                if (List == null)
                {
                    return true;
                }
                else
                {
                    return !List.IsReadOnly;
                }
            }
        }

        /// <summary>
        /// True if the collection view says it can sort.
        /// </summary>
        public bool AllowSort
        {
            get
            {
                if (CollectionView == null ||
                    (EditableCollectionView != null && (EditableCollectionView.IsAddingNew || EditableCollectionView.IsEditingItem)))
                {
                    return false;
                }
                else
                {
                    return CollectionView.CanSort;
                }
            }
        }

        public bool CommittingEdit
        {
            get;
            private set;
        }

        public int Count => TryGetCount(true, false, out var count) ? count : 0;

        public bool DataIsPrimitive
        {
            get
            {
                return DataTypeIsPrimitive(DataType);
            }
        }

        public PropertyInfo[] DataProperties
        {
            get
            {
                if (_dataProperties == null)
                {
                    UpdateDataProperties();
                }
                return _dataProperties;
            }
        }

        public IEnumerable DataSource
        {
            get
            {
                return _dataSource;
            }
            set
            {
                _dataSource = value;
                // Because the DataSource is changing, we need to reset our cached values for DataType and DataProperties,
                // which are dependent on the current DataSource
                _dataType = null;
                UpdateDataProperties();
            }
        }

        public Type DataType
        {
            get
            {
                // We need to use the raw ItemsSource as opposed to DataSource because DataSource
                // may be the ItemsSource wrapped in a collection view, in which case we wouldn't
                // be able to take T to be the type if we're given IEnumerable<T>
                if (_dataType == null && _owner.ItemsSource != null)
                {
                    _dataType = _owner.ItemsSource.GetItemType();
                }
                return _dataType;
            }
        }

        public bool EventsWired
        {
            get;
            private set;
        }

        private bool IsGrouping
        {
            get
            {
                return (CollectionView != null)
                    && CollectionView.CanGroup
                    && CollectionView.IsGrouping
                    && (CollectionView.GroupingDepth > 0);
            }
        }

        public IList List
        {
            get
            {
                return DataSource as IList;
            }
        }

        public bool ShouldAutoGenerateColumns
        {
            get
            {
                return false;
            }
        }

        public IDataGridCollectionView CollectionView
        {
            get
            {
                return DataSource as IDataGridCollectionView;
            }
        }
        public IDataGridEditableCollectionView EditableCollectionView
        {
            get
            {
                return DataSource as IDataGridEditableCollectionView;
            }
        }

        public DataGridSortDescriptionCollection SortDescriptions
        {
            get
            {
                if (CollectionView != null && CollectionView.CanSort)
                {
                    return CollectionView.SortDescriptions;
                }
                else
                {
                    return null;
                }
            }
        }

        private bool CanAddNew
        {
            get
            {
                if (!_owner.CanUserAddRows)
                    return false;
                if (EditableCollectionView == null)
                    return false;
                if (EditableCollectionView is DataGridCollectionView dataGridCollectionView && !dataGridCollectionView.SourceCanAddNew)
                    return false;
                if (EditableCollectionView.IsAddingNew)
                    return false;

                return true;
            }
        }

        public bool CanRemove
        {
            get => _owner.CanUserDeleteRows
                && (
                    (EditableCollectionView?.CanRemove ?? false)
                    || (!List?.IsReadOnly ?? false)
                );
        }

        /// <summary>Try get number of DataSource items.</summary>
        /// <param name="allowSlow">When "allowSlow" is false, method will not use Linq.Count() method and will return 0 or 1 instead.</param>
        /// <param name="getAny">If "getAny" is true, method can use Linq.Any() method to speedup.</param>
        /// <param name="count">number of DataSource items.</param>
        /// <returns>true if able to retrieve number of DataSource items; otherwise, false.</returns>
        internal bool TryGetCount(bool allowSlow, bool getAny, out int count)
        {
            bool result;
            (result, count) = DataSource switch
            {
                ICollection collection => (true, collection.Count),
                IEnumerable enumerable when allowSlow && !getAny => (true, enumerable.Cast<object>().Count()),
                IEnumerable enumerable when getAny => (true, enumerable.Cast<object>().Any() ? 1 : 0),
                _ => (false, 0)
            };
            if (result && CanAddNew)
                count++;

            return result;
        }

        internal bool Any()
        {
            bool result = TryGetCount(false, true, out var count);
            if (result && CanAddNew)
                count--;
            return result && count > 0;
        }

        /// <summary>
        /// Puts the entity into editing mode if possible
        /// </summary>
        /// <param name="dataItem">The entity to edit</param>
        /// <returns>True if editing was started</returns>
        public bool BeginEdit(object dataItem)
        {
            if (dataItem == null)
            {
                return false;
            }

            IDataGridEditableCollectionView editableCollectionView = EditableCollectionView;
            if (editableCollectionView != null)
            {
                if (editableCollectionView.IsEditingItem && (dataItem == editableCollectionView.CurrentEditItem))
                {
                    return true;
                }
                else
                {
                    if (dataItem == DataGridCollectionView.NewItemPlaceholder)
                    {
                        _onCollectionChangeAddRemoveNewRowPlaceholder = true;
                        editableCollectionView.AddNew();
                        _onCollectionChangeAddRemoveNewRowPlaceholder = false;
                    }
                    else
                        editableCollectionView.EditItem(dataItem);
                    return editableCollectionView.IsEditingItem || editableCollectionView.IsAddingNew;
                }
            }

            if (dataItem is IEditableObject editableDataItem)
            {
                editableDataItem.BeginEdit();
                return true;
            }

            return true;
        }

        /// <summary>
        /// Cancels the current entity editing and exits the editing mode.
        /// </summary>
        /// <param name="dataItem">The entity being edited</param>
        /// <returns>True if a cancellation operation was invoked.</returns>
        public bool CancelEdit(object dataItem)
        {
            IDataGridEditableCollectionView editableCollectionView = EditableCollectionView;
            if (editableCollectionView != null)
            {
                if (editableCollectionView.CanCancelEdit)
                {
                    editableCollectionView.CancelEdit();
                    return true;
                }
                else if (editableCollectionView.IsAddingNew)
                {
                    //while we are not techincally committing it supresses relevant changes (row being removed)
                    CommittingEdit = true;
                    editableCollectionView.CancelNew();
                    CommittingEdit = false;
                    //now add the blank new row back in 
                    AddNewItemRow(_owner.SlotCount);
                    return true;
                }
                return false;
            }

            if (dataItem is IEditableObject editableDataItem)
            {
                editableDataItem.CancelEdit();
                return true;
            }

            return true;
        }

        public static bool CanEdit(Type type)
        {
            Debug.Assert(type != null);

            type = type.GetNonNullableType();

            return
                type.IsEnum
                || type == typeof(System.String)
                || type == typeof(System.Char)
                || type == typeof(System.DateTime)
                || type == typeof(System.Boolean)
                || type == typeof(System.Byte)
                || type == typeof(System.SByte)
                || type == typeof(System.Single)
                || type == typeof(System.Double)
                || type == typeof(System.Decimal)
                || type == typeof(System.Int16)
                || type == typeof(System.Int32)
                || type == typeof(System.Int64)
                || type == typeof(System.UInt16)
                || type == typeof(System.UInt32)
                || type == typeof(System.UInt64);
        }

        /// <summary>
        /// Commits the current entity editing and exits the editing mode.
        /// </summary>
        /// <param name="dataItem">The entity being edited</param>
        /// <returns>True if a commit operation was invoked.</returns>
        public bool EndEdit(object dataItem)
        {
            IDataGridEditableCollectionView editableCollectionView = EditableCollectionView;
            if (editableCollectionView != null)
            {
                // IEditableCollectionView.CommitEdit can potentially change currency. If it does,
                // we don't want to attempt a second commit inside our CurrentChanging event handler.
                _owner.NoCurrentCellChangeCount++;
                CommittingEdit = true;
                try
                {
                    if (editableCollectionView.IsAddingNew)
                    {
                        editableCollectionView.CommitNew();
                        AddNewItemRow(_owner.SlotCount);
                    }
                    else
                    {
                        editableCollectionView.CommitEdit();
                    }                    
                }
                finally
                {
                    _owner.NoCurrentCellChangeCount--;
                    CommittingEdit = false;
                }
                return true;
            }

            if (dataItem is IEditableObject editableDataItem)
            {
                editableDataItem.EndEdit();
            }

            return true;
        }

        // Assumes index >= 0, returns null if index >= Count
        public object GetDataItem(int index)
        {
            Debug.Assert(index >= 0);

            if (DataSource is DataGridCollectionView collectionView)
            {
                int count = collectionView.Count;
                if (index < count)
                    return collectionView.GetItemAt(index);
                else if (index == count)
                    return DataGridCollectionView.NewItemPlaceholder;
                else
                    return null;
            }

            IList list = List;
            if (list != null)
            {
                return (index < list.Count) ? list[index] : null;
            }

            IEnumerable enumerable = DataSource;
            if (enumerable != null)
            {
                IEnumerator enumerator = enumerable.GetEnumerator();
                int i = -1;
                while (enumerator.MoveNext() && i < index)
                {
                    i++;
                    if (i == index)
                    {
                        return enumerator.Current;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Check if a property is readonly only (has a setter, isn't <see cref="EditableAttribute.AllowEdit"/>
        /// and that the property is an editable basic type)
        /// </summary>
        /// <param name="propertyName">The name or a path to the property</param>
        /// <remarks>
        /// To check if the property can be written too and not check it's type use <see cref="GetPropertyIsReadOnly(string, out Type)"/>
        /// </remarks>
        public bool GetPropertyIsReadOnly(string propertyName)
        {
            if (DataType != null)
            {
                if (!String.IsNullOrEmpty(propertyName))
                {
                    if (GetPropertyIsReadOnly(propertyName, out Type propertyType))
                        return true;

                    return !AllowEdit || !CanEdit(propertyType);
                }
                else if (DataType.GetIsReadOnly())
                {
                    return true;
                }
            }
            return !AllowEdit;
        }

        /// <summary>
        /// Check if a property is readonly based on if it has a setter
        /// </summary>
        /// <param name="propertyName">The name or a path to the property</param>
        /// <param name="propertyType">The property type found via the path</param>
        public bool GetPropertyIsReadOnly(string propertyName, out Type propertyType)
        {
            propertyType = null;
            if (DataType == null)
                return !AllowEdit;

            if (string.IsNullOrEmpty(propertyName))
                return DataType.GetIsReadOnly();

            propertyType = DataType;
            PropertyInfo propertyInfo = null;
            List<string> propertyNames = TypeHelper.SplitPropertyPath(propertyName);
            for (int i = 0; i < propertyNames.Count; i++)
            {
                propertyInfo = propertyType.GetPropertyOrIndexer(propertyNames[i], out _);
                if (propertyInfo == null || propertyType.GetIsReadOnly() || propertyInfo.GetIsReadOnly())
                {
                    // Either the data type is read-only, the property doesn't exist, or it does exist but is read-only
                    return true;
                }

                // Check if EditableAttribute is defined on the property and if it indicates uneditable
                var attributes = propertyInfo.GetCustomAttributes(typeof(EditableAttribute), true);
                if (attributes != null && attributes.Length > 0)
                {
                    var editableAttribute = (EditableAttribute)attributes[0];
                    if (!editableAttribute.AllowEdit)
                    {
                        return true;
                    }
                }
                propertyType = propertyInfo.PropertyType.GetNonNullableType();
            }
            return propertyInfo == null || !propertyInfo.CanWrite;
        }

        public int IndexOf(object dataItem)
        {
            bool isNewItem = dataItem == DataGridCollectionView.NewItemPlaceholder;

            if (DataSource is DataGridCollectionView cv)
            {
                return isNewItem ? cv.Count : cv.IndexOf(dataItem);
            }

            IList list = List;
            if (list != null)
            {
                return isNewItem ? list.Count : list.IndexOf(dataItem);
            }

            IEnumerable enumerable = DataSource;
            if (enumerable != null && dataItem != null)
            {
                if (isNewItem)
                {
                    int count = 0;
                    foreach (object item in enumerable)
                        count++;
                    return count;
                }

                int index = 0;
                foreach (object dataItemTmp in enumerable)
                {
                    if ((dataItem == null && dataItemTmp == null) ||
                        dataItem.Equals(dataItemTmp))
                    {
                        return index;
                    }
                    index++;
                }
            }
            return -1;
        }

        internal void ClearDataProperties()
        {
            _dataProperties = null;
        }

        /// <summary>
        /// Creates a collection view around the DataGrid's source. ICollectionViewFactory is
        /// used if the source implements it. Otherwise a PagedCollectionView is returned.
        /// </summary>
        /// <param name="source">Enumerable source for which to create a view</param>
        /// <returns>ICollectionView view over the provided source</returns>
        internal static IDataGridCollectionView CreateView(IEnumerable source)
        {
            Debug.Assert(source != null, "source unexpectedly null");
            Debug.Assert(!(source is IDataGridCollectionView), "source is an ICollectionView");

            IDataGridCollectionView collectionView = null;

            if (source is IDataGridCollectionViewFactory collectionViewFactory)
            {
                // If the source is a collection view factory, give it a chance to produce a custom collection view.
                collectionView = collectionViewFactory.CreateView();
                // Intentionally not catching potential exception thrown by ICollectionViewFactory.CreateView().
            }
            if (collectionView == null)
            {
                // If we still do not have a collection view, default to a PagedCollectionView.
                collectionView = new DataGridCollectionView(source);
            }
            return collectionView;
        }

        internal static bool DataTypeIsPrimitive(Type dataType)
        {
            if (dataType != null)
            {
                Type type = TypeHelper.GetNonNullableType(dataType);  // no-opt if dataType isn't nullable
                return type.IsPrimitive || type == typeof(string) || type == typeof(DateTime) || type == typeof(Decimal);
            }
            else
            {
                return false;
            }
        }

        internal void MoveCurrentTo(object item, int backupSlot, int columnIndex, DataGridSelectionAction action, bool scrollIntoView)
        {
            if (CollectionView != null)
            {
                _expectingCurrentChanged = true;
                _columnForCurrentChanged = columnIndex;
                _itemToSelectOnCurrentChanged = item;
                _selectionActionForCurrentChanged = action;
                _scrollForCurrentChanged = scrollIntoView;
                _backupSlotForCurrentChanged = backupSlot;

                //if its the new item pretend the collection has changed so the selection gets updated
                if (item == DataGridCollectionView.NewItemPlaceholder)
                    CollectionView_CurrentChanged(this, EventArgs.Empty);
                else
                    CollectionView.MoveCurrentTo(item is DataGridCollectionViewGroup ? null : item);

                _expectingCurrentChanged = false;
            }
        }

        internal void UnWireEvents(IEnumerable value)
        {
            if (value is INotifyCollectionChanged notifyingDataSource)
            {
                notifyingDataSource.CollectionChanged -= NotifyingDataSource_CollectionChanged;
            }

            if (SortDescriptions != null)
            {
                SortDescriptions.CollectionChanged -= CollectionView_SortDescriptions_CollectionChanged;
            }

            if (CollectionView != null)
            {
                CollectionView.CurrentChanged -= CollectionView_CurrentChanged;
                CollectionView.CurrentChanging -= CollectionView_CurrentChanging;
            }

            EventsWired = false;
        }

        internal void WireEvents(IEnumerable value)
        {
            if (value is INotifyCollectionChanged notifyingDataSource)
            {
                notifyingDataSource.CollectionChanged += NotifyingDataSource_CollectionChanged;
            }

            if (SortDescriptions != null)
            {
                SortDescriptions.CollectionChanged += CollectionView_SortDescriptions_CollectionChanged;
            }

            if (CollectionView != null)
            {
                CollectionView.CurrentChanged += CollectionView_CurrentChanged;
                CollectionView.CurrentChanging += CollectionView_CurrentChanging;
            }

            EventsWired = true;
        }

        private void CollectionView_CurrentChanged(object sender, EventArgs e)
        {
            if (_expectingCurrentChanged)
            {
                // Committing Edit could cause our item to move to a group that no longer exists.  In
                // this case, we need to update the item.
                if (_itemToSelectOnCurrentChanged is DataGridCollectionViewGroup collectionViewGroup)
                {
                    DataGridRowGroupInfo groupInfo = _owner.RowGroupInfoFromCollectionViewGroup(collectionViewGroup);
                    if (groupInfo == null)
                    {
                        // Move to the next slot if the target slot isn't visible                        
                        if (!_owner.IsSlotVisible(_backupSlotForCurrentChanged))
                        {
                            _backupSlotForCurrentChanged = _owner.GetNextVisibleSlot(_backupSlotForCurrentChanged);
                        }
                        // Move to the next best slot if we've moved past all the slots.  This could happen if multiple
                        // groups were removed.
                        if (_backupSlotForCurrentChanged >= _owner.SlotCount)
                        {
                            _backupSlotForCurrentChanged = _owner.GetPreviousVisibleSlot(_owner.SlotCount);
                        }
                        // Update the itemToSelect
                        int newCurrentPosition = -1;
                        _itemToSelectOnCurrentChanged = _owner.ItemFromSlot(_backupSlotForCurrentChanged, ref newCurrentPosition);
                    }
                }

                _owner.ProcessSelectionAndCurrency(
                    _columnForCurrentChanged,
                    _itemToSelectOnCurrentChanged,
                    _backupSlotForCurrentChanged,
                    _selectionActionForCurrentChanged,
                    _scrollForCurrentChanged);
            }
            else if (CollectionView != null)
            {
                _owner.UpdateStateOnCurrentChanged(CollectionView.CurrentItem, CollectionView.CurrentPosition);
            }
        }

        private void CollectionView_CurrentChanging(object sender, DataGridCurrentChangingEventArgs e)
        {
            if (_owner.NoCurrentCellChangeCount == 0 &&
                !_expectingCurrentChanged &&
                !CommittingEdit &&
                !_owner.CommitEdit())
            {
                // If CommitEdit failed, then the user has most likely input invalid data.
                // We should cancel the current change if we can, otherwise we have to abort the edit.
                if (e.IsCancelable)
                {
                    e.Cancel = true;
                }
                else
                {
                    _owner.CancelEdit(DataGridEditingUnit.Row, false);
                }
            }
        }

        private void CollectionView_SortDescriptions_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (_owner.ColumnsItemsInternal.Count == 0)
            {
                return;
            }

            // refresh sort description
            foreach (DataGridColumn column in _owner.ColumnsItemsInternal)
            {
                column.HeaderCell.UpdatePseudoClasses();
            }
        }

        private void NotifyingDataSource_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (_owner.LoadingOrUnloadingRow)
            {
                throw DataGridError.DataGrid.CannotChangeItemsWhenLoadingRows();
            }
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    Debug.Assert(e.NewItems != null, "Unexpected NotifyCollectionChangedAction.Add notification");
                    if (ShouldAutoGenerateColumns)
                    {
                        // The columns are also affected (not just rows) in this case so we need to reset everything
                        _owner.InitializeElements(false /*recycleRows*/);
                    }
                    else if (!IsGrouping)
                    {
                        // If we're grouping then we handle this through the CollectionViewGroup notifications
                        // According to WPF, Add is a single item operation
                        Debug.Assert(e.NewItems.Count == 1);
                        if (_onCollectionChangeAddRemoveNewRowPlaceholder)
                            _owner.RemoveRowAt(e.NewStartingIndex, IndexOf(DataGridCollectionView.NewItemPlaceholder));
                        _owner.InsertRowAt(e.NewStartingIndex);
                    }
                    break;
                case NotifyCollectionChangedAction.Remove:
                    IList removedItems = e.OldItems;
                    if (removedItems == null || e.OldStartingIndex < 0)
                    {
                        Debug.Assert(false, "Unexpected NotifyCollectionChangedAction.Remove notification");
                        return;
                    }
                    if (!IsGrouping)
                    {
                        // If we're grouping then we handle this through the CollectionViewGroup notifications
                        // According to WPF, Remove is a single item operation
                        foreach (object item in e.OldItems)
                        {
                            Debug.Assert(item != null);
                            _owner.RemoveRowAt(e.OldStartingIndex, item);
                        }
                    }
                    break;
                case NotifyCollectionChangedAction.Replace:
                    throw new NotSupportedException(); // 

                case NotifyCollectionChangedAction.Reset:
                    // Did the data type change during the reset?  If not, we can recycle
                    // the existing rows instead of having to clear them all.  We still need to clear our cached
                    // values for DataType and DataProperties, though, because the collection has been reset.
                    Type previousDataType = _dataType;
                    _dataType = null;
                    if (previousDataType != DataType)
                    {
                        ClearDataProperties();
                        _owner.InitializeElements(false /*recycleRows*/);
                    }
                    else
                    {
                        _owner.InitializeElements(!ShouldAutoGenerateColumns /*recycleRows*/);
                    }
                    break;
            }

            _owner.UpdatePseudoClasses();
        }

        private void UpdateDataProperties()
        {
            Type dataType = DataType;

            if (DataSource != null && dataType != null && !DataTypeIsPrimitive(dataType))
            {
                _dataProperties = dataType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                Debug.Assert(_dataProperties != null);
            }
            else
            {
                _dataProperties = null;
            }
        }

        private void AddNewItemRow(int slot)
        {
            _owner.InsertRowAt(slot);
        }

        /// <summary>
        /// Remove an item from the underlying <see cref="DataSource"/>
        /// </summary>
        /// <returns></returns>
        internal void Remove(object item)
        {
            if (EditableCollectionView != null)
                EditableCollectionView.Remove(item);
            else if (List != null && !List.IsReadOnly)
                List.Remove(item);
            else
                throw new NotSupportedException("Underlaying data source does not support modifications");
        }
    }
}
