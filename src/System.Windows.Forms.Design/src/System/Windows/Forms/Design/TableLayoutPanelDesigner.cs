using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Design;
using System.Text;
using System.Windows.Forms.Design.Behavior;

namespace System.Windows.Forms.Design
{
    /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner"]/*' />
    /// <devdoc>
    ///     This class handles all design time behavior for the TableLayoutPanel class.  This class
    ///     is in charge of: making sure all rows/columns are correctly rendered, all drag/move
    ///     gestures correctly add/insert controls into the table, and new controls are added to the
    ///     appropriate cell(s).
    /// </devdoc>
    internal class TableLayoutPanelDesigner : FlowPanelDesigner
    {

        private TableLayoutPanelBehavior tlpBehavior;//every resize col/row glyph is associated with this instance of behavior
        private Point droppedCellPosition = InvalidPoint;//used to insert new children

        // NEVER USE undoing DIRECTLY. ALWAYS USE THE PROPERTY
        private bool undoing;
        private UndoEngine undoEngine;

        private Control localDragControl;//only valid if we're currently dragging a child control of the table
        private ArrayList dragComps;          //the components we are dragging
        private DesignerVerbCollection verbs;//add col/row and remove col/row tab verbs
        private DesignerTableLayoutControlCollection controls;
        private DesignerVerb removeRowVerb;
        private DesignerVerb removeColVerb;
        DesignerActionListCollection actionLists;//action list for the Smart Tag

        private BaseContextMenuStrip designerContextMenuStrip;
        private int curRow = -1; //row cursor was over when context menu was dropped
        private int curCol = -1;  //col cursor was over when context menu was dropped

        IComponentChangeService compSvc;
        private PropertyDescriptor rowStyleProp;
        private PropertyDescriptor colStyleProp;

        // Only used when adding controls via the toolbox
        private int rowCountBeforeAdd; // What's the row count before a control is added
        private int colCountBeforeAdd; // Ditto for column

        // TLP context menu row/column items.
        private ToolStripMenuItem contextMenuRow;
        private ToolStripMenuItem contextMenuCol;


        private int ensureSuspendCount;

        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.Behavior"]/*' />
        /// <devdoc>
        ///     The instance to our Behavior that every glyph shares.  This is
        ///     demand created only once.
        /// </devdoc>
        private TableLayoutPanelBehavior Behavior
        {
            get
            {
                if (tlpBehavior == null)
                {
                    tlpBehavior = new TableLayoutPanelBehavior(Table, this, Component.Site);
                }
                return tlpBehavior;
            }
        }


        // SPECIAL HANDLING  - VSWhidbey 483915
        //
        // The only reason we shadow ColumnStyles and RowStyles is to control
        // how they are serialized out. If the form is not localizable, then we 
        // want to serialize to code. If the form is localized, then we do not
        // want to serialize to code. This is because the TLP has special logic
        // that adds these properties to the resx file in this case. It needs this
        // special code, since the properties are not marked as localizable.
        // If we also added these to the code, we would actually end up with double
        // the number of styles in the collections.
        //

        // TLP has no setter
        private TableLayoutColumnStyleCollection ColumnStyles
        {
            get
            {
                return Table.ColumnStyles;
            }
        }

        // TLP has no setter
        private TableLayoutRowStyleCollection RowStyles
        {
            get
            {
                return Table.RowStyles;
            }
        }

        public int RowCount
        {
            get
            {
                return Table.RowCount;
            }
            set
            {
                if (value <= 0 && !Undoing)
                {
                    throw new ArgumentException(SR.GetString(SR.TableLayoutPanelDesignerInvalidColumnRowCount, "RowCount"));
                }
                else
                {
                    Table.RowCount = value;
                }
            }
        }

        public int ColumnCount
        {
            get
            {
                return Table.ColumnCount;
            }
            set
            {
                if (value <= 0 && !Undoing)
                {
                    throw new ArgumentException(SR.GetString(SR.TableLayoutPanelDesignerInvalidColumnRowCount, "ColumnCount"));
                }
                else
                {
                    Table.ColumnCount = value;
                }
            }
        }

        private bool IsLocalizable()
        {
            IDesignerHost host = GetService(typeof(IDesignerHost)) as IDesignerHost;
            if (host != null)
            {
                PropertyDescriptor prop = TypeDescriptor.GetProperties(host.RootComponent)["Localizable"];
                if (prop != null && prop.PropertyType == typeof(bool))
                {
                    return (bool)prop.GetValue(host.RootComponent);
                }
            }

            return false;
        }

        private bool ShouldSerializeColumnStyles()
        {
            return !IsLocalizable();
        }

        private bool ShouldSerializeRowStyles()
        {
            return !IsLocalizable();
        }

        // END SPECIAL HANDLING

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        private DesignerTableLayoutControlCollection Controls
        {
            get
            {
                if (controls == null)
                {
                    controls = new DesignerTableLayoutControlCollection((TableLayoutPanel)Control);
                }
                return controls;
            }
        }

        private ContextMenuStrip DesignerContextMenuStrip
        {
            get
            {
                if (designerContextMenuStrip == null)
                {
                    designerContextMenuStrip = new BaseContextMenuStrip(Component.Site, Table);

                    // Remove all the verbs -- except the Edit Rows and Columns
                    ContextMenuStripGroup group = designerContextMenuStrip.Groups[StandardGroups.Verbs];
                    foreach (DesignerVerb verb in Verbs)
                    {
                        if (verb.Text.Equals(SR.GetString(SR.TableLayoutPanelDesignerEditRowAndCol)))
                        {
                            continue;
                        }

                        foreach (ToolStripItem item in group.Items)
                        {
                            if (item.Text.Equals(verb.Text))
                            {
                                group.Items.Remove(item);
                                break;
                            }
                        }
                    }

                    // Now build the new menus
                    ToolStripDropDownMenu rowMenu = BuildMenu(true);
                    ToolStripDropDownMenu colMenu = BuildMenu(false);

                    this.contextMenuRow = new ToolStripMenuItem();
                    this.contextMenuRow.DropDown = rowMenu;
                    this.contextMenuRow.Text = SR.GetString(SR.TableLayoutPanelDesignerRowMenu);

                    this.contextMenuCol = new ToolStripMenuItem();
                    this.contextMenuCol.DropDown = colMenu;
                    this.contextMenuCol.Text = SR.GetString(SR.TableLayoutPanelDesignerColMenu);

                    group.Items.Insert(0, contextMenuCol);
                    group.Items.Insert(0, contextMenuRow);

                    group = designerContextMenuStrip.Groups[StandardGroups.Edit];
                    foreach (ToolStripItem item in group.Items)
                    {
                        if (item.Text.Equals(SR.GetString(SR.ContextMenuCut)))
                        {
                            item.Text = SR.GetString(SR.TableLayoutPanelDesignerContextMenuCut);
                        }
                        else if (item.Text.Equals(SR.GetString(SR.ContextMenuCopy)))
                        {
                            item.Text = SR.GetString(SR.TableLayoutPanelDesignerContextMenuCopy);
                        }
                        else if (item.Text.Equals(SR.GetString(SR.ContextMenuDelete)))
                        {
                            item.Text = SR.GetString(SR.TableLayoutPanelDesignerContextMenuDelete);
                        }
                    }
                }

                bool onValidCell = IsOverValidCell(false);

                this.contextMenuRow.Enabled = onValidCell;
                this.contextMenuCol.Enabled = onValidCell;

                return designerContextMenuStrip;
            }
        }


        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.IsLoading"]/*' />
        /// <devdoc>
        ///     Returns true if the host is loading.
        /// </devdoc>
        private bool IsLoading
        {
            get
            {
                IDesignerHost host = GetService(typeof(IDesignerHost)) as IDesignerHost;

                if (host != null)
                {
                    return host.Loading;
                }

                return false;
            }
        }

        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.Table"]/*' />
        /// <devdoc>
        ///     Returns our runtime TableLayoutPanel instance.
        /// </devdoc>
        internal TableLayoutPanel Table
        {
            get
            {
                return Component as TableLayoutPanel;
            }
        }


        private bool Undoing
        {
            get
            {
                if (undoEngine == null)
                {
                    undoEngine = GetService(typeof(UndoEngine)) as UndoEngine;
                    if (undoEngine != null)
                    {
                        undoEngine.Undoing += new EventHandler(this.OnUndoing);
                        if (undoEngine.UndoInProgress)
                        {
                            undoing = true;
                            undoEngine.Undone += new EventHandler(this.OnUndone);
                        }
                    }
                }

                return undoing;
            }

            set
            {
                undoing = value;
            }
        }



        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.Verbs"]/*' />
        /// <devdoc>
        ///     The designer verbs we'll surface for this table.
        /// </devdoc>

        // We need to add separate verbs (and not use the ones from the DesignerActionPanel), since
        // we need to be able to disable/enable the Remove items based on the number of rows/cols, and
        // you cannot do that via the DesignerAction stuff.

        // These verbs would be promoted to the DAP, but we need a DAP since we want it to AutoShow.
        public override DesignerVerbCollection Verbs
        {
            get
            {
                if (verbs == null)
                {
                    removeColVerb = new DesignerVerb(SR.GetString(SR.TableLayoutPanelDesignerRemoveColumn), new EventHandler(this.OnVerbRemove));
                    removeRowVerb = new DesignerVerb(SR.GetString(SR.TableLayoutPanelDesignerRemoveRow), new EventHandler(this.OnVerbRemove));

                    verbs = new DesignerVerbCollection();

                    verbs.Add(new DesignerVerb(SR.GetString(SR.TableLayoutPanelDesignerAddColumn), new EventHandler(this.OnVerbAdd)));
                    verbs.Add(new DesignerVerb(SR.GetString(SR.TableLayoutPanelDesignerAddRow), new EventHandler(this.OnVerbAdd)));
                    verbs.Add(removeColVerb);
                    verbs.Add(removeRowVerb);
                    verbs.Add(new DesignerVerb(SR.GetString(SR.TableLayoutPanelDesignerEditRowAndCol), new EventHandler(this.OnVerbEdit)));

                    CheckVerbStatus();
                }

                return verbs;
            }
        }

        private void RefreshSmartTag()
        {
            DesignerActionUIService actionUIService = (DesignerActionUIService)GetService(typeof(DesignerActionUIService));
            if (actionUIService != null)
            {
                actionUIService.Refresh(Component);
            }
        }

        private void CheckVerbStatus()
        {
            if (Table != null)
            {
                if (removeColVerb != null)
                {

                    bool colState = Table.ColumnCount > 1;
                    if (removeColVerb.Enabled != colState)
                    {
                        removeColVerb.Enabled = colState;
                    }
                }

                if (removeRowVerb != null)
                {
                    bool rowState = Table.RowCount > 1;
                    if (removeRowVerb.Enabled != rowState)
                    {
                        removeRowVerb.Enabled = rowState;
                    }
                }

                RefreshSmartTag();
            }
        }


        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.ActionLists"]/*' />
        /// <devdoc>
        ///    <para>Gets the design-time action lists supported by the component associated with the
        ///       designer.</para>
        /// </devdoc>
        public override DesignerActionListCollection ActionLists
        {
            get
            {
                if (actionLists == null)
                {
                    BuildActionLists();
                }

                return actionLists;
            }
        }

        private ToolStripDropDownMenu BuildMenu(bool isRow)
        {

            ToolStripMenuItem add = new ToolStripMenuItem();
            ToolStripMenuItem insert = new ToolStripMenuItem();
            ToolStripMenuItem delete = new ToolStripMenuItem();
            ToolStripSeparator separator = new ToolStripSeparator();
            ToolStripLabel label = new ToolStripLabel();
            ToolStripMenuItem absolute = new ToolStripMenuItem();
            ToolStripMenuItem percent = new ToolStripMenuItem();
            ToolStripMenuItem autosize = new ToolStripMenuItem();

            add.Text = SR.GetString(SR.TableLayoutPanelDesignerAddMenu);
            add.Tag = isRow;
            add.Name = "add";
            add.Click += new System.EventHandler(this.OnAddClick);

            insert.Text = SR.GetString(SR.TableLayoutPanelDesignerInsertMenu);
            insert.Tag = isRow;
            insert.Name = "insert";
            insert.Click += new System.EventHandler(this.OnInsertClick);

            delete.Text = SR.GetString(SR.TableLayoutPanelDesignerDeleteMenu);
            delete.Tag = isRow;
            delete.Name = "delete";
            delete.Click += new System.EventHandler(this.OnDeleteClick);

            label.Text = SR.GetString(SR.TableLayoutPanelDesignerLabelMenu);
            if (SR.GetString(SR.TableLayoutPanelDesignerDontBoldLabel) == "0")
            {
                label.Font = new Font(label.Font, FontStyle.Bold);
            }

            label.Name = "sizemode";

            absolute.Text = SR.GetString(SR.TableLayoutPanelDesignerAbsoluteMenu);
            absolute.Tag = isRow;
            absolute.Name = "absolute";
            absolute.Click += new System.EventHandler(this.OnAbsoluteClick);

            percent.Text = SR.GetString(SR.TableLayoutPanelDesignerPercentageMenu);
            percent.Tag = isRow;
            percent.Name = "percent";
            percent.Click += new System.EventHandler(this.OnPercentClick);

            autosize.Text = SR.GetString(SR.TableLayoutPanelDesignerAutoSizeMenu);
            autosize.Tag = isRow;
            autosize.Name = "autosize";
            autosize.Click += new System.EventHandler(this.OnAutoSizeClick);

            ToolStripDropDownMenu menu = new ToolStripDropDownMenu();
            menu.Items.AddRange(new ToolStripItem[] { add, insert, delete, separator, label, absolute, percent, autosize });
            menu.Tag = isRow;
            menu.Opening += new System.ComponentModel.CancelEventHandler(this.OnRowColMenuOpening);

            IUIService uis = GetService(typeof(IUIService)) as IUIService;
            if (uis != null)
            {
                menu.Renderer = (ToolStripProfessionalRenderer)uis.Styles["VsRenderer"];
                if (uis.Styles["VsColorPanelText"] is Color)
                {
                    menu.ForeColor = (Color)uis.Styles["VsColorPanelText"];
                }
            }
            return menu;

        }

        private void BuildActionLists()
        {
            actionLists = new DesignerActionListCollection();

            // Add Column action list
            actionLists.Add(new TableLayouPanelRowColumnActionList(this));

            // if one actionList has AutoShow == true then the chrome panel will popup when the user DnD the DataGridView onto the form
            // It would make sense to promote AutoShow to DesignerActionListCollection.
            // But we don't own the DesignerActionListCollection so we just set AutoShow on the first ActionList
            //
            actionLists[0].AutoShow = true;
        }

        private class TableLayouPanelRowColumnActionList : DesignerActionList
        {

            TableLayoutPanelDesigner owner;

            public TableLayouPanelRowColumnActionList(TableLayoutPanelDesigner owner) : base(owner.Component)
            {
                this.owner = owner;
            }

            public override DesignerActionItemCollection GetSortedActionItems()
            {
                DesignerActionItemCollection items = new DesignerActionItemCollection();

                // We don't promote these Items to DesignerVerbs, since we need to be able
                // to disable/enable the Remove entries, based on the number of Rows/Cols.
                // Unfortunately, you cannot do that via the DesignerAction stuff.
                items.Add(new DesignerActionMethodItem(this,
                                                         "AddColumn", // method name
                                                          SR.GetString(SR.TableLayoutPanelDesignerAddColumn), // display name
                                                          false));                                             // promoteToDesignerVerb
                items.Add(new DesignerActionMethodItem(this,
                                                         "AddRow", // method name
                                                          SR.GetString(SR.TableLayoutPanelDesignerAddRow),  // display name
                                                          false));                                           // promoteToDesignerVerb

                if (owner.Table.ColumnCount > 1)
                {
                    items.Add(new DesignerActionMethodItem(this,
                                                         "RemoveColumn", // method name
                                                          SR.GetString(SR.TableLayoutPanelDesignerRemoveColumn), // display name
                                                          false));                                                // promoteToDesignerVerb
                }

                if (owner.Table.RowCount > 1)
                {
                    items.Add(new DesignerActionMethodItem(this,
                                                         "RemoveRow", // method name
                                                          SR.GetString(SR.TableLayoutPanelDesignerRemoveRow),  // display name
                                                          false));                                                // promoteToDesignerVerb
                }

                items.Add(new DesignerActionMethodItem(this,
                                                         "EditRowAndCol", // method name
                                                          SR.GetString(SR.TableLayoutPanelDesignerEditRowAndCol),  // display name
                                                          false));                                                // promoteToDesignerVerb

                return items;
            }

            public void AddColumn()
            {
                this.owner.OnAdd(false);
            }

            public void AddRow()
            {
                this.owner.OnAdd(true);
            }

            public void RemoveColumn()
            {
                this.owner.OnRemove(false);
            }

            public void RemoveRow()
            {
                this.owner.OnRemove(true);
            }

            public void EditRowAndCol()
            {
                this.owner.OnEdit();
            }
        }



        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.RemoveControlInternal"]/*' />
        /// <devdoc>
        ///     Called privately - we'll un-sync the removed event and quietly remove the control
        //      from the table.
        /// </devdoc>
        private void RemoveControlInternal(Control c)
        {
            Table.ControlRemoved -= new ControlEventHandler(this.OnControlRemoved);
            Table.Controls.Remove(c);
            Table.ControlRemoved += new ControlEventHandler(this.OnControlRemoved);
        }

        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.AddControlInternal"]/*' />
        /// <devdoc>
        ///     Called within this designer - this method will un-sync the
        ///     'ControlAdded' event and add a control (aboslutely positioned)
        ///     to the row and column specified.
        /// </devdoc>
        private void AddControlInternal(Control c, int col, int row)
        {
            Table.ControlAdded -= new ControlEventHandler(this.OnControlAdded);
            Table.Controls.Add(c, col, row);
            Table.ControlAdded += new ControlEventHandler(this.OnControlAdded);
        }


        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.ControlAddedInternal"]/*' />
        /// <devdoc>
        ///     Called in response to controls being added - through dragging, copy/paste, toolbox, etc...
        ///     This method removes the control and re-adds it back to the table at an absolute
        ///     position.  This is done for more of a deterministic behavior when rows/cols change in
        ///     our table.
        /// </devdoc>
        // Standard 'catch all - rethrow critical' exception pattern
        private void ControlAddedInternal(Control control, Point newControlPosition, bool localReposition, bool fullTable, DragEventArgs de)
        {

            //If the table is full - we'll want to 'autogrow' either the row or column based on the grow style property
            //before we actually add the control.
            if (fullTable)
            {
                if (Table.GrowStyle == TableLayoutPanelGrowStyle.AddRows)
                {

                    PropertyDescriptor rowProp = TypeDescriptor.GetProperties(Table)["RowCount"];
                    if (rowProp != null)
                    {
                        rowProp.SetValue(Table, Table.GetRowHeights().Length);
                    }
                    newControlPosition.X = 0;
                    newControlPosition.Y = Table.RowCount - 1;
                }
                else if (Table.GrowStyle == TableLayoutPanelGrowStyle.AddColumns)
                {

                    PropertyDescriptor colProp = TypeDescriptor.GetProperties(Table)["ColumnCount"];
                    if (colProp != null)
                    {
                        colProp.SetValue(Table, Table.GetColumnWidths().Length);
                    }

                    newControlPosition.X = Table.ColumnCount - 1;
                    newControlPosition.Y = 0;
                }
                else
                {
                    //fixed growstyle - what do we do here?
                }
            }
            //Here, we are adding a control to our table - we need to make sure we
            // 1) correctly place the new control at its drop location, and
            // 2) perform a 'swap' if this is entirely a local
            // reposition.

            DesignerTransaction trans = null;
            PropertyDescriptor controlsProp = TypeDescriptor.GetProperties(Table)["Controls"];
            //find the control that currently resides at our newControlPosition - we'll want to either
            //remove it or swap it.
            try
            {

                // Are we doing a local copy
                bool localCopy = ((de != null) && (de.Effect == DragDropEffects.Copy) && localReposition);

                Control existingControl = ((TableLayoutPanel)Control).GetControlFromPosition(newControlPosition.X, newControlPosition.Y);

                if (localCopy)
                {
                    Debug.Assert(existingControl == null, "We shouldn't be able to do a local copy of a cell with an existing control");
                    IDesignerHost host = GetService(typeof(IDesignerHost)) as IDesignerHost;
                    if (host != null)
                    {
                        trans = host.CreateTransaction(SR.GetString(SR.BehaviorServiceCopyControl, control.Site.Name));
                    }
                    // Need to do this after the transaction is created
                    PropChanging(controlsProp);
                }
                //does the newControlPosition contain a valid control
                //if so - we need to perform a 'swap' function if this is local - or default
                //to controls.add if this is from an external source
                else if (existingControl != null && !existingControl.Equals(control))
                {
                    if (localReposition)
                    {
                        // If we're swapping controls, create a DesignerTransaction
                        // so this can be undoable.
                        IDesignerHost host = GetService(typeof(IDesignerHost)) as IDesignerHost;
                        if (host != null)
                        {
                            trans = host.CreateTransaction(SR.GetString(SR.TableLayoutPanelDesignerControlsSwapped, control.Site.Name, existingControl.Site.Name));
                        }
                        // Need to do this after the transaction is created
                        PropChanging(controlsProp);
                        RemoveControlInternal(existingControl);//we found our control to swap
                    }
                    else
                    {
                        //here we externally dragged a control onto a valid control in our table
                        //we'll try to find a place to put it (since we shouldn't be here if our table
                        //was full

                        // MartinTh -- we shouldn't ever get here...
                        PropChanging(controlsProp);
                        existingControl = null;//null this out since we're not swapping
                    }
                }
                else
                {
                    //here we have a truly empty cell

                    // If we are not doing a local move, then the DropSourceBehavior created the transaction for us
                    if (localReposition)
                    {
                        IDesignerHost host = GetService(typeof(IDesignerHost)) as IDesignerHost;
                        if (host != null)
                        {
                            trans = host.CreateTransaction(SR.GetString(SR.BehaviorServiceMoveControl, control.Site.Name));
                        }
                    }

                    existingControl = null;
                    PropChanging(controlsProp);
                }


                //Need to do this after the transaction has been created
                if (localCopy)
                {
                    ArrayList temp = new ArrayList();
                    temp.Add(control);
                    temp = DesignerUtils.CopyDragObjects(temp, Component.Site) as ArrayList;
                    control = temp[0] as Control;
                }


                //if we are locally repositioning this control - remove it (internally)
                //from the table's child collection and add something in its place.  This
                //will be a control to swap it with
                if (localReposition)
                {
                    Point oldPosition = GetControlPosition(control);
                    if (oldPosition != InvalidPoint)
                    {
                        RemoveControlInternal(control);

                        if (oldPosition != newControlPosition)
                        {//guard against dropping it back on itself
                            if (existingControl != null)
                            {
                                //we have something to swap...
                                AddControlInternal(existingControl, oldPosition.X, oldPosition.Y);
                            }
                        }
                    }
                }

                //Finally - set our new control to the new position
                if (localReposition)
                {
                    //If we are doing a local drag, then the control previously got removed
                    AddControlInternal(control, newControlPosition.X, newControlPosition.Y);
                }
                else
                {
                    //If not, then the control has already been added, and all we need to do is set the position
                    Table.SetCellPosition(control, new TableLayoutPanelCellPosition(newControlPosition.X, newControlPosition.Y));
                }

                PropChanged(controlsProp);

                if (de != null)
                {
                    base.OnDragComplete(de);
                }

                if (trans != null)
                {
                    trans.Commit();
                    trans = null;
                }

                // Set the selection to be the newly added control - but only if we are doing a local copy
                if (localCopy)
                {
                    ISelectionService selSvc = GetService(typeof(ISelectionService)) as ISelectionService;
                    if (selSvc != null)
                    {
                        selSvc.SetSelectedComponents(new object[] { control }, SelectionTypes.Primary | SelectionTypes.Replace);
                    }
                }


            }

            catch (ArgumentException argumentEx)
            {
                IUIService uiService = GetService(typeof(IUIService)) as IUIService;
                if (uiService != null)
                {
                    uiService.ShowError(argumentEx);
                }
            }

            catch (Exception ex)
            {
                if (ClientUtils.IsCriticalException(ex))
                {
                    throw;
                }
            }

            finally
            {
                if (trans != null)
                {
                    trans.Cancel();
                }
            }

        }


        private void CreateEmptyTable()
        {

            //set the table's default rows and columns
            PropertyDescriptor colProp = TypeDescriptor.GetProperties(Table)["ColumnCount"];
            if (colProp != null)
            {
                colProp.SetValue(Table, DesignerUtils.DEFAULTCOLUMNCOUNT);
            }

            PropertyDescriptor rowProp = TypeDescriptor.GetProperties(Table)["RowCount"];
            if (rowProp != null)
            {
                rowProp.SetValue(Table, DesignerUtils.DEFAULTROWCOUNT);
            }

            //this will make sure we have styles created for every row & column
            EnsureAvailableStyles();

            InitializeNewStyles();

        }

        private void InitializeNewStyles()
        {

            //adjust the two absolutely positioned columns
            Table.ColumnStyles[0].SizeType = SizeType.Percent;
            Table.ColumnStyles[0].Width = DesignerUtils.MINIMUMSTYLEPERCENT;
            Table.ColumnStyles[1].SizeType = SizeType.Percent;
            Table.ColumnStyles[1].Width = DesignerUtils.MINIMUMSTYLEPERCENT;

            //adjust two absolutely positioned rows
            Table.RowStyles[0].SizeType = SizeType.Percent;
            Table.RowStyles[0].Height = DesignerUtils.MINIMUMSTYLEPERCENT;
            Table.RowStyles[1].SizeType = SizeType.Percent;
            Table.RowStyles[1].Height = DesignerUtils.MINIMUMSTYLEPERCENT;
        }

        /// <summary>
        /// Returns true if an empty subset of size subsetColumns x subsetRows exists in the cells
        /// array. cells[c,r] == true if the corresponding cell contains a control
        /// </summary>
        /// <param name="cells"></param>
        /// <param name="columns"></param>
        /// <param name="rows"></param>
        /// <param name="subsetColumns"></param>
        /// <param name="subsetRows"></param>
        /// <returns></returns>
        private static bool SubsetExists(bool[,] cells, int columns, int rows, int subsetColumns, int subsetRows)
        {
            bool exists = false;
            int column = 0;
            int row;

            for (row = 0; row < rows - subsetRows + 1; row++)
            {
                for (column = 0; column < columns - subsetColumns + 1; column++)
                {
                    if (!cells[column, row])
                    {
                        exists = true;
                        for (int m = row; (m < row + subsetRows) && exists; m++)
                        {
                            for (int n = column; n < column + subsetColumns; n++)
                            {
                                if (cells[n, m])
                                {
                                    exists = false;
                                    break;
                                }
                            }
                        }
                        if (exists)
                        {
                            break;
                        }

                    }
                }
                if (exists)
                {
                    break;
                }
            }

            return exists;
        }


        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.Dispose"]/*' />
        /// <devdoc>
        ///      Test if the component can be added to the table. In the case the table can't grow, we need
        ///      to make sure that the table has enough empty cells for the new components to be added and that the 
        ///      new component can fit into the available cells. For example, if the component spans 3 rows 
        ///      but the only available cells are lined up in a single row, we can't add the component.
        ///      This check is needed because this transaction throws on Cancel, (and thus can't be un-done) 
        ///      because the available rows and columns cache in TableLayoutPanel runtime object is not refreshed
        ///      when adding a new component fails(Dev10 462169).
        /// </devdoc>
        protected internal override bool CanAddComponent(IComponent component)
        {
            if (Table.GrowStyle != TableLayoutPanelGrowStyle.FixedSize)
            {
                return true;
            }

            Control newControl = GetControl(component);
            if (newControl == null)
            {
                // this case should have been filtered out by CanParent
                return false;
            }

            int rowSpan = Table.GetRowSpan(newControl);
            int columnSpan = Table.GetColumnSpan(newControl);

            // under certain conditions RowCount and ColumnCount are not accurate
            int numRows = Table.GetRowHeights().Length;
            int numColumns = Table.GetColumnWidths().Length;
            int numOccupiedCells = 0; // total occupied cells in the TableLayoutPanel

            int totalCells = numRows * numColumns;
            int cellsNeeded = rowSpan * columnSpan;

            // cache which cells have controls in them
            bool[,] occupiedCells = null;
            if (cellsNeeded > 1)
            {
                occupiedCells = new bool[numColumns, numRows];
            }

            if (cellsNeeded <= totalCells)
            {
                for (int row = 0; row < numRows; row++)
                {
                    for (int column = 0; column < numColumns; column++)
                    {
                        if (null != Table.GetControlFromPosition(column, row))
                        {
                            numOccupiedCells++;
                            if (cellsNeeded > 1)
                            {
                                occupiedCells[column, row] = true;
                            }
                        }
                    }
                }
            }

            // Check if the table has enough empty cells to accomodate the new component
            if (numOccupiedCells + cellsNeeded > totalCells)
            {
                IUIService uiService = (IUIService)GetService(typeof(IUIService));
                uiService.ShowError(SR.GetString(SR.TableLayoutPanelFullDesc));

                return false;
            }

            // if the new control spans several rows or columns, check if the 
            // table has a contiguous free area to accomodate the control
            if (cellsNeeded > 1)
            {

                if (!SubsetExists(occupiedCells, numColumns, numRows, columnSpan, rowSpan))
                {
                    IUIService uiService = (IUIService)GetService(typeof(IUIService));
                    uiService.ShowError(SR.GetString(SR.TableLayoutPanelSpanDesc));

                    return false;
                }
            }

            return true;
        }

        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.Dispose"]/*' />
        /// <devdoc>
        ///      Disposes of this object and all events and cached services.
        /// </devdoc>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {

                IDesignerHost host = (IDesignerHost)GetService(typeof(IDesignerHost));
                if (host != null)
                {
                    host.TransactionClosing -= new DesignerTransactionCloseEventHandler(this.OnTransactionClosing);
                }

                if (undoEngine != null)
                {
                    if (Undoing)
                    {
                        undoEngine.Undone -= new EventHandler(this.OnUndone);
                    }
                    undoEngine.Undoing -= new EventHandler(this.OnUndoing);
                }

                if (compSvc != null)
                {
                    compSvc.ComponentChanged -= new ComponentChangedEventHandler(this.OnComponentChanged);
                    compSvc.ComponentChanging -= new ComponentChangingEventHandler(this.OnComponentChanging);
                }

                if (Table != null)
                {
                    Table.ControlAdded -= new ControlEventHandler(this.OnControlAdded);
                    Table.ControlRemoved -= new ControlEventHandler(this.OnControlRemoved);
                }

                if (contextMenuRow != null)
                {
                    contextMenuRow.Dispose();
                }

                if (contextMenuCol != null)
                {
                    contextMenuCol.Dispose();
                }

                rowStyleProp = null;
                colStyleProp = null;

            }

            base.Dispose(disposing);
        }

        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.DrawBorder"]/*' />
        /// <devdoc>
        ///     This draws a nice border around our panel and each cell only
        ///     if the cell border styles are not set or 'none'.
        /// </devdoc>
        protected override void DrawBorder(Graphics graphics)
        {

            if (Table.CellBorderStyle != TableLayoutPanelCellBorderStyle.None)
            {
                //only draw a fake border if there is no borderstyle
                return;
            }

            base.DrawBorder(graphics);

            Rectangle rc = Control.DisplayRectangle;
            rc.Width--;
            rc.Height--;

            int[] cw = Table.GetColumnWidths();
            int[] rh = Table.GetRowHeights();

            using (Pen pen = BorderPen)
            {
                if (cw.Length > 1)
                {

                    bool isRTL = (Table.RightToLeft == RightToLeft.Yes);
                    // offset by padding
                    int startX = isRTL ? rc.Right : rc.Left;
                    for (int i = 0; i < cw.Length - 1; i++)
                    {

                        if (isRTL)
                        {
                            startX -= cw[i];
                        }
                        else
                        {
                            startX += cw[i];
                        }

                        graphics.DrawLine(pen, startX, rc.Top, startX, rc.Bottom);
                    }
                }

                if (rh.Length > 1)
                {
                    int startY = rc.Top;
                    for (int i = 0; i < rh.Length - 1; i++)
                    {
                        startY += rh[i];
                        graphics.DrawLine(pen, rc.Left, startY, rc.Right, startY);
                    }
                }
            }
        }


        //The StyleCollectionEditor uses these to make sure that the Styles collection
        //doesn't change underneath us
        internal void SuspendEnsureAvailableStyles()
        {
            ensureSuspendCount++;
        }

        //The StyleCollectionEditor uses these to make sure that the Styles collection
        //doesn't change underneath us
        internal void ResumeEnsureAvailableStyles(bool performEnsure)
        {
            if (ensureSuspendCount > 0)
            {
                ensureSuspendCount--;

                if (ensureSuspendCount == 0 && performEnsure)
                {
                    EnsureAvailableStyles();
                }
            }
        }

        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.EnsureAvailableStyles2"]/*' />
        /// <devdoc>
        ///     This is method ensures that for every row and column our
        ///     runtime control has - that we have appropriate row and column
        ///     styles for them.
        /// </devdoc>
        private bool EnsureAvailableStyles()
        {

            if (IsLoading || Undoing || ensureSuspendCount > 0)
            {
                return false;
            }

            int[] cw = Table.GetColumnWidths();
            int[] rh = Table.GetRowHeights();

            Table.SuspendLayout();
            try
            {
                //if we have more columns then column styles add some...
                if (cw.Length > Table.ColumnStyles.Count)
                {
                    int colDifference = cw.Length - Table.ColumnStyles.Count;
                    PropChanging(rowStyleProp);
                    for (int i = 0; i < colDifference; i++)
                    {
                        Table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DesignerUtils.MINIMUMSTYLESIZE));

                    }
                    PropChanged(rowStyleProp);
                }

                //if we have more rows then row styles add some...
                if (rh.Length > Table.RowStyles.Count)
                {
                    int rowDifference = rh.Length - Table.RowStyles.Count;
                    PropChanging(colStyleProp);
                    for (int i = 0; i < rowDifference; i++)
                    {
                        Table.RowStyles.Add(new RowStyle(SizeType.Absolute, DesignerUtils.MINIMUMSTYLESIZE));
                    }
                    PropChanged(colStyleProp);
                }
            }
            finally
            {
                Table.ResumeLayout();
            }
            return true;
        }


        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.ExtractControlFromDragEvent"]/*' />
        /// <devdoc>
        ///    Takes a drageventargs and extracts the 0th dragging control.
        /// </devdoc>
        private Control ExtractControlFromDragEvent(DragEventArgs de)
        {

            DropSourceBehavior.BehaviorDataObject data = de.Data as DropSourceBehavior.BehaviorDataObject;
            if (data != null)
            {
                dragComps = new ArrayList(data.DragComponents);
                return dragComps[0] as Control;
            }

            return null;
        }


        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.GetCellPosition"]/*' />
        /// <devdoc>
        ///     Called on drag-drop operations, give the point passed in,
        ///     this method will attempt to find the appropriate cell.
        ///     If the point is below col/row 0, the corresponding coordintate will be -1.
        ///     If the point is above the upper col/row, the corresponding coordintate will be upper + 1.
        /// </devdoc>
        private Point GetCellPosition(Point pos)
        {
            //get some runtime table info
            int[] rows = Table.GetRowHeights();
            int[] columns = Table.GetColumnWidths();

            //By using DisplayRectangle here we handle the case where we are scrolled. VSWhidbey #399557
            Point startingPoint = Table.PointToScreen(Table.DisplayRectangle.Location);
            Rectangle bounds = new Rectangle(startingPoint, Table.DisplayRectangle.Size);

            Point position = new Point(-1, -1);

            bool isRTL = Table.RightToLeft == RightToLeft.Yes;
            int offset = bounds.X;

            // find column ...
            if (isRTL)
            {
                if (pos.X <= bounds.X)
                { // if pos.X >= bounds.Right, position.X = -1
                    position.X = columns.Length;
                }
                else if (pos.X < bounds.Right)
                { // it must be within the bounds
                    offset = bounds.Right;

                    //loop through the columns and identify where the mouse is
                    for (int i = 0; i < columns.Length; i++)
                    {
                        position.X = i;
                        if (pos.X >= offset - columns[i])
                        {
                            break;
                        }
                        offset -= columns[i];
                    }
                }
            }
            else
            {
                if (pos.X >= bounds.Right)
                {
                    position.X = columns.Length;
                }
                else if (pos.X > bounds.X)
                { // if pos.X <= bounds.X, position.X = -1.
                  //loop through the columns and identify where the mouse is
                    for (int i = 0; i < columns.Length; i++)
                    { // it must be within the bounds
                        position.X = i;
                        if (pos.X <= offset + columns[i])
                        {
                            break;
                        }
                        offset += columns[i];
                    }
                }
            }

            // find row ...
            offset = bounds.Y;

            if (pos.Y >= bounds.Bottom)
            {
                position.Y = rows.Length;
            }
            else if (pos.Y > bounds.Y)
            { // if pos.Y <= bounds.Y, position.Y = -1
                //loop through the rows and identify where the mouse is
                for (int i = 0; i < rows.Length; i++)
                {
                    if (pos.Y <= offset + rows[i])
                    {
                        position.Y = i;
                        break;
                    }

                    offset += rows[i];
                }
            }

            return position;
        }


        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.GetControlPosition"]/*' />
        /// <devdoc>
        ///     Reverse lookup into our table.  Given a control we'll find
        ///     the location (colxrow) in the table.
        /// </devdoc>
        private Point GetControlPosition(Control control)
        {

            TableLayoutPanelCellPosition pos = Table.GetPositionFromControl(control);
            if ((pos.Row == -1) && (pos.Column == -1))
            {
                return InvalidPoint;
            }

            return new Point(pos.Column, pos.Row);
        }


        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.GetGlyphs"]/*' />
        /// <devdoc>
        ///     This method will add glyphs for every row and column in our Table.
        /// </devdoc>
        public override GlyphCollection GetGlyphs(GlyphSelectionType selectionType)
        {
            GlyphCollection glyphs = base.GetGlyphs(selectionType);

            PropertyDescriptor prop = TypeDescriptor.GetProperties(Component)["Locked"];
            bool locked = (prop != null) ? ((bool)prop.GetValue(Component)) : false;

            //Before adding glyphs for every row/column, make sure we have a column/rowstyle for every column/row
            bool safeToRefresh = EnsureAvailableStyles();

            //if we're somehow selected, not locked, and not inherited -then offer up glyphs for every
            //column/row line
            if (selectionType != GlyphSelectionType.NotSelected && !locked && InheritanceAttribute != InheritanceAttribute.InheritedReadOnly)
            {

                //get the correctly translated bounds

                //By using DisplayRectangle here we handle the case where we are scrolled. VSWhidbey #399689
                Point loc = BehaviorService.MapAdornerWindowPoint(Table.Handle, Table.DisplayRectangle.Location);
                Rectangle bounds = new Rectangle(loc, Table.DisplayRectangle.Size);

                Point controlLoc = BehaviorService.ControlToAdornerWindow(Control);
                Rectangle checkBounds = new Rectangle(controlLoc, Control.ClientSize); // Can't use Control.Size since that will include any scrollbar


                int[] cw = Table.GetColumnWidths();
                int[] rh = Table.GetRowHeights();
                int halfSize = DesignerUtils.RESIZEGLYPHSIZE / 2;

                bool isRTL = (Table.RightToLeft == RightToLeft.Yes);
                int startLoc = isRTL ? bounds.Right : bounds.X;

                if (safeToRefresh)
                {


                    //add resize glyphs for each column and row
                    for (int i = 0; i < cw.Length - 1; i++)
                    {
                        //Do not add a glyph for columns of 0 width. This can happen for percentage columns, where the table is not
                        //big enough for there to be any space for percentage columns
                        if (cw[i] == 0)
                        {
                            continue;
                        }

                        if (isRTL)
                        {
                            startLoc -= cw[i];
                        }
                        else
                        {
                            startLoc += cw[i];//x offset of column line
                        }

                        Rectangle gBounds = new Rectangle(startLoc - halfSize, checkBounds.Top, DesignerUtils.RESIZEGLYPHSIZE, checkBounds.Height);
                        //Don't add glyphs for columns that are not within the clientrectangle
                        if (!checkBounds.Contains(gBounds))
                        {
                            continue;
                        }

                        Debug.Assert(Table.ColumnStyles[i] != null, "Table's ColumnStyle[" + i + "] is null!");
                        if (Table.ColumnStyles[i] != null)
                        {
                            TableLayoutPanelResizeGlyph g = new TableLayoutPanelResizeGlyph(gBounds, Table.ColumnStyles[i], Cursors.VSplit, Behavior);
                            glyphs.Add(g);
                        }
                    }

                    startLoc = bounds.Y;//reset for the rows...

                    for (int i = 0; i < rh.Length - 1; i++)
                    {
                        //Do not add a glyph for rows of 0 height. This can happen for percentage columns, where the table is not
                        //big enough for there to be any space for percentage columns
                        if (rh[i] == 0)
                        {
                            continue;
                        }

                        startLoc += rh[i];//y offset of row line
                        Rectangle gBounds = new Rectangle(checkBounds.Left, startLoc - halfSize, checkBounds.Width, DesignerUtils.RESIZEGLYPHSIZE);
                        if (!checkBounds.Contains(gBounds))
                        {
                            continue;
                        }

                        Debug.Assert(Table.RowStyles[i] != null, "Table's RowStyle[" + i + "] is null!");
                        if (Table.RowStyles[i] != null)
                        {
                            TableLayoutPanelResizeGlyph g = new TableLayoutPanelResizeGlyph(gBounds, Table.RowStyles[i], Cursors.HSplit, Behavior);
                            glyphs.Add(g);
                        }
                    }
                }
            }

            return glyphs;
        }


        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.Initialize"]/*' />
        /// <devdoc>
        ///      Overrides the base and syncs load, change, and child added events.
        /// </devdoc>
        public override void Initialize(IComponent component)
        {
            base.Initialize(component);

            IDesignerHost host = (IDesignerHost)GetService(typeof(IDesignerHost));
            if (host != null)
            {
                host.TransactionClosing += new DesignerTransactionCloseEventHandler(this.OnTransactionClosing);
                compSvc = host.GetService(typeof(IComponentChangeService)) as IComponentChangeService;
            }

            if (compSvc != null)
            {
                compSvc.ComponentChanging += new ComponentChangingEventHandler(this.OnComponentChanging);
                compSvc.ComponentChanged += new ComponentChangedEventHandler(this.OnComponentChanged);
            }

            Control.ControlAdded += new ControlEventHandler(this.OnControlAdded);
            Control.ControlRemoved += new ControlEventHandler(this.OnControlRemoved);


            rowStyleProp = TypeDescriptor.GetProperties(Table)["RowStyles"];
            colStyleProp = TypeDescriptor.GetProperties(Table)["ColumnStyles"];

            if (InheritanceAttribute == InheritanceAttribute.InheritedReadOnly)
            {
                for (int i = 0; i < Control.Controls.Count; i++)
                {
                    TypeDescriptor.AddAttributes(Control.Controls[i], InheritanceAttribute.InheritedReadOnly);
                }
            }



        }

        protected override InheritanceAttribute InheritanceAttribute
        {
            get
            {
                if ((base.InheritanceAttribute == InheritanceAttribute.Inherited)
                    || (base.InheritanceAttribute == InheritanceAttribute.InheritedReadOnly))
                {
                    return InheritanceAttribute.InheritedReadOnly;
                }
                return base.InheritanceAttribute;
            }
        }

        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.InitializeNewComponent"]/*' />
        /// <devdoc>
        ///      After a new comp is initialized - make sure our table has 2 rows and 2 columsn.
        /// </devdoc>
        public override void InitializeNewComponent(IDictionary defaultValues)
        {
            base.InitializeNewComponent(defaultValues);
            CreateEmptyTable();
        }

        // We override this to find out if the table is full BEFORE the control is created.
        protected override IComponent[] CreateToolCore(ToolboxItem tool, int x, int y, int width, int height, bool hasLocation, bool hasSize)
        {

            rowCountBeforeAdd = Math.Max(0, Table.GetRowHeights().Length); // don't want negative
            colCountBeforeAdd = Math.Max(0, Table.GetColumnWidths().Length);

            return base.CreateToolCore(tool, x, y, width, height, hasLocation, hasSize);
        }

        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.OnControlAdded"]/*' />
        /// <devdoc>
        ///     When a new child is added - we check to see if we have cached a cell pos.
        ///     If so, we'll re-insert the control at this new position.
        /// </devdoc>
        private void OnControlAdded(object sender, ControlEventArgs e)
        {

            if (IsLoading || Undoing)
            {
                return;
            }

            // Calculate the number cells spanned by controls in the Table 

            // This can be slow, but it is the only way to really calculate the number of cells spanned.
            // We cannot rely on checking the control's span since the TLP's growstyle might affect it.
            // E.g. RowCount = ColumnCount = 2, GrowStyle = AddRows, button in cell(0,0), button.ColumnSpan = 6
            int totalArea = 0;
            int[] rows = Table.GetRowHeights();
            int[] columns = Table.GetColumnWidths();

            for (int row = 0; row < rows.Length; row++)
            {
                for (int column = 0; column < columns.Length; column++)
                {
                    if (Table.GetControlFromPosition(column, row) != null)
                    {
                        ++totalArea;
                    }
                }
            }

            //The control we are about to place, have already been added to the TLP's control collection, so -1 here.
            //This is because we want to know if the table was full BEFORE the control was added.

            bool fullTable = (totalArea - 1) >= (Math.Max(1, colCountBeforeAdd) * Math.Max(1, rowCountBeforeAdd));

            if (droppedCellPosition == InvalidPoint)
            {
                droppedCellPosition = GetControlPosition(e.Control);
            }

            Debug.Assert(fullTable || (droppedCellPosition != InvalidPoint), "Why is neither fullTable or droppedCellPosition set?");

            ControlAddedInternal(e.Control, droppedCellPosition, false, fullTable, null);

            droppedCellPosition = InvalidPoint;


        }

        private void OnControlRemoved(object sender, ControlEventArgs e)
        {
            // Need to do this to make sure undo/redo works
            // Since the Row/Col extended property is DesignerSerializationVisibility.Hidden, the undo engine
            // will not serialize the value out, so we need to reset it here. VSWhidbey #392705.
            if (e != null && e.Control != null)
            {
                Table.SetCellPosition(e.Control, new TableLayoutPanelCellPosition(-1, -1));
            }
        }

        private bool IsOverValidCell(bool dragOp)
        {

            Point dropPoint = GetCellPosition(Control.MousePosition);

            // check if cell position is valid.
            int[] rows = Table.GetRowHeights();
            int[] columns = Table.GetColumnWidths();

            if (dropPoint.Y < 0 || dropPoint.Y >= rows.Length || dropPoint.X < 0 || dropPoint.X >= columns.Length)
            {
                return false;
            }

            if (dragOp)
            {
                Control existingControl = (Control)((TableLayoutPanel)Control).GetControlFromPosition(dropPoint.X, dropPoint.Y);

                //If the cell is not empty, and we are not doing a local drag, then show the no-smoking cursor
                //or if we are doing a multi-select local drag, then show the no-smoking cursor.
                //or if we are doig a local drag, and the cell is not empty, and we are doing a copy
                if ((existingControl != null && localDragControl == null) ||
                    (localDragControl != null && dragComps.Count > 1) ||
                    (localDragControl != null && existingControl != null && Control.ModifierKeys == Keys.Control))
                {
                    return false;
                }
            }

            return true;
        }

        protected override void OnContextMenu(int x, int y)
        {
            Point cell = GetCellPosition(new Point(x, y));

            curRow = cell.Y;
            curCol = cell.X;

            //Set the SizeMode correctly
            EnsureAvailableStyles();

            DesignerContextMenuStrip.Show(x, y);
        }

        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.OnDragEnter"]/*' />
        /// <devdoc>
        ///     Fired when we get a drag enter - here, we'll check it out
        ///     to see if we're dragging one of our child controls.
        /// </devdoc>
        protected override void OnDragEnter(DragEventArgs de)
        {

            base.OnDragEnter(de);

            //peak at what just entered her e- it it's a local control
            //we'll cache it off
            if (localDragControl == null)
            {
                Control dragControl = ExtractControlFromDragEvent(de);
                if (dragControl != null && Table.Controls.Contains(dragControl))
                {
                    localDragControl = dragControl;
                }
            }

        }


        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.OnDragLeave"]/*' />
        /// <devdoc>
        ///     Called when a drag-drop operation leaves the control designer view
        ///
        /// </devdoc>
        protected override void OnDragLeave(EventArgs e)
        {
            localDragControl = null;
            dragComps = null;
            base.OnDragLeave(e);
        }



        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.OnDragDrop"]/*' />
        /// <devdoc>
        ///    When a control is dropped we first determine which cell is related to the
        ///    drop position.  After this, if the control that was dropped is an existing
        ///    child control of our Table - we remove and add it back at its new
        ///    position.  Otherwise, we'll do this in the OnChildAdded event.
        /// </devdoc>
        protected override void OnDragDrop(DragEventArgs de)
        {

            droppedCellPosition = GetCellPosition(Control.MousePosition);

            //the scenario where we just dropped our own child control
            if (localDragControl != null)
            {
                //local drag to our TLP - we need to re-insert or swap it...
                ControlAddedInternal(localDragControl, droppedCellPosition, true, false, de);
                localDragControl = null;
            }
            else
            {
                rowCountBeforeAdd = Math.Max(0, Table.GetRowHeights().Length); // don't want negative
                colCountBeforeAdd = Math.Max(0, Table.GetColumnWidths().Length);

                //If from the outside, just let the base class handle it
                base.OnDragDrop(de);

                // This will not fix in the copy/paste scenario but it will for the
                // main drag/drop scneario.  
                // We need to do this after the controls are added (after base.OnDragDrop above)
                // because Span is an "attached" property which is not available unless the control is parented to
                // a table. However in the case when the table is full and can't grow, setting control's 
                // span after the Add is too late, because the runtime had already thrown an exception.
                // Unfortunally cancelling the transaction throws as well, so we need a way to undo the Add
                // or access internal properties of the control.

                // dragComps is null when dragging off the toolbox
                if (dragComps != null)
                {
                    foreach (Control dragControl in dragComps)
                    {
                        if (dragControl != null)
                        {
                            PropertyDescriptor columnSpan = TypeDescriptor.GetProperties(dragControl)["ColumnSpan"];
                            PropertyDescriptor rowSpan = TypeDescriptor.GetProperties(dragControl)["RowSpan"];
                            if (columnSpan != null)
                            {
                                columnSpan.SetValue(dragControl, 1);
                            }
                            if (rowSpan != null)
                            {
                                rowSpan.SetValue(dragControl, 1);
                            }
                        }
                    }
                }
            }

            droppedCellPosition = InvalidPoint;
            dragComps = null;
        }


        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.OnDragOver"]/*' />
        /// <devdoc>
        ///    If our table is full on drag enter -then we should not allow any
        ///    drop operations on our surface.
        /// </devdoc>
        protected override void OnDragOver(DragEventArgs de)
        {

            // If we are not over a valid cell, then do not allow the drop
            if (!IsOverValidCell(true))
            {
                de.Effect = DragDropEffects.None;
                return;
            }

            base.OnDragOver(de);
        }

        private Dictionary<string, bool> extenderProperties;

        private Dictionary<string, bool> ExtenderProperties
        {
            get
            {
                if (extenderProperties == null && Component != null)
                {
                    extenderProperties = new Dictionary<string, bool>();

                    AttributeCollection attribs = TypeDescriptor.GetAttributes(Component.GetType());

                    foreach (Attribute a in attribs)
                    {
                        ProvidePropertyAttribute extender = a as ProvidePropertyAttribute;
                        if (extender != null)
                        {
                            extenderProperties[extender.PropertyName] = true;
                        }
                    }

                }
                return extenderProperties;
            }
        }

        /// <devdoc>
        ///    Checks if the given memberdescriptor represents an extender property offered
        ///    by the TLP that affects layout and is not normally serialized (Row, Column, etc.)
        ///    The UndoEngine will ignore these properties, so we need to force seralization.
        /// </devdoc>
        private bool DoesPropertyAffectPosition(MemberDescriptor member)
        {
            bool affectsPosition = false;
            DesignerSerializationVisibilityAttribute dsv = member.Attributes[typeof(DesignerSerializationVisibilityAttribute)] as DesignerSerializationVisibilityAttribute;
            if (dsv != null)
            {
                affectsPosition = dsv.Visibility == DesignerSerializationVisibility.Hidden && ExtenderProperties.ContainsKey(member.Name);
            }

            return affectsPosition;
        }

        private void OnComponentChanging(object sender, ComponentChangingEventArgs e)
        {
            Control changingControl = e.Component as Control;

            if (changingControl != null && changingControl.Parent == this.Component &&
                e.Member != null && DoesPropertyAffectPosition(e.Member))
            {

                PropertyDescriptor controlsProp = TypeDescriptor.GetProperties(Component)["Controls"];
                compSvc.OnComponentChanging(Component, controlsProp);

            }

        }

        private void OnComponentChanged(object sender, ComponentChangedEventArgs e)
        {
            //When the Row or Column property is being set on a control in the TLP, a Row/Col Style is not being added.
            //After the property is being set, the SelectionManager::OnSelectionChanged gets called. It in turn calls
            //GetGlyphs, and the TLP designer's GetGlyphs calls EnsureAvaiableStyles. Since no style was added, we will add
            //a default one of type Absolute, Height/Width 20. But but but... If the control has added its glyph before we
            //add the style, the glyphs wil be misaligned, since EnsureAvailableStyles also causes the TLP to do a layout. This
            //layout will actually size the control to a smaller size. So let's trap the Row/Col property changing, call
            //EnsureAvailableStyles, which will force the layout BEFORE the SelectionManager is called.
            if (e.Component != null)
            {
                Control c = e.Component as Control;
                if (c != null && c.Parent != null && c.Parent.Equals(Control) && e.Member != null && (e.Member.Name == "Row" || e.Member.Name == "Column"))
                {
                    EnsureAvailableStyles();
                }

                if (c != null && c.Parent == this.Component &&
                    e.Member != null && DoesPropertyAffectPosition(e.Member))
                {

                    PropertyDescriptor controlsProp = TypeDescriptor.GetProperties(Component)["Controls"];
                    compSvc.OnComponentChanged(Component, controlsProp, null, null);

                }
            }

            CheckVerbStatus();
        }


        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="ParentControlDesigner.OnTransactionClosing"]/*' />
        /// <devdoc>
        ///     Called when a designer transaction is closing (finishing).  We sync this
        ///     so that we can refresh our tablelayoutpanel control.  Calling suspend and
        ///     resume layout here will essentially invoke the internal 'onlayout' method
        ///     on our control to force proper layout.
        /// </devdoc>
        private void OnTransactionClosing(object sender, DesignerTransactionCloseEventArgs e)
        {
            ISelectionService selSvc = GetService(typeof(ISelectionService)) as ISelectionService;
            if (selSvc != null && Table != null)
            {
                ICollection selectedComps = selSvc.GetSelectedComponents();
                bool selectedComponentHasTableParent = false;
                foreach (object comp in selectedComps)
                {
                    Control c = comp as Control;
                    if (c != null && c.Parent == Table)
                    {
                        selectedComponentHasTableParent = true;
                        break;
                    }
                }

                if (selSvc.GetComponentSelected(Table) || selectedComponentHasTableParent)
                {
                    //force an internal 'onlayout' event to refresh our control
                    Table.SuspendLayout();
                    EnsureAvailableStyles();
                    Table.ResumeLayout(false);
                    Table.PerformLayout();
                }
            }
        }

        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="ParentControlDesigner.OnUndoing"]/*' />
        /// <devdoc>
        ///     Called before undo starts .  We make sure to suspend designer
        ///     operations during undo.
        /// </devdoc>
        private void OnUndoing(object sender, EventArgs e)
        {
            if (!Undoing)
            {
                if (undoEngine != null)
                {
                    undoEngine.Undone += new EventHandler(this.OnUndone);
                }
                Undoing = true;
            }
        }

        /// <include file='doc\ParentControlDesigner.uex' path='docs/doc[@for="ParentControlDesigner.OnLoadComplete"]/*' />
        /// <devdoc>
        ///     Called when undo ends.  We'll make sure to resume
        ///     unsink our Undone event and also refresh the designer.
        /// </devdoc>
        private void OnUndone(object sender, EventArgs e)
        {
            if (Undoing)
            {
                if (undoEngine != null)
                {
                    undoEngine.Undone -= new EventHandler(this.OnUndone);
                }
                Undoing = false;

                bool isSafeToRefresh = EnsureAvailableStyles();

                if (isSafeToRefresh)
                {
                    Refresh();
                }
            }
        }


        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.OnMouseDragEnd"]/*' />
        /// <devdoc>
        ///     If the user has drawn a rectangle with a toolbox item,
        ///     we'll cache off the cell in which it happened so we can
        ///     add it @ the right location in the event OnChildAdded.
        /// </devdoc>
        protected override void OnMouseDragBegin(int x, int y)
        {

            if (IsOverValidCell(true))
            {
                //make sure we have a valid toolbox item and we're not just drawing a rect
                IToolboxService tbx = (IToolboxService)GetService(typeof(IToolboxService));
                if (tbx != null && tbx.GetSelectedToolboxItem((IDesignerHost)GetService(typeof(IDesignerHost))) != null)
                {
                    droppedCellPosition = GetCellPosition(Control.MousePosition);
                }
            }
            else
            {
                droppedCellPosition = InvalidPoint;
                Cursor.Current = Cursors.No;
            }

            base.OnMouseDragBegin(x, y);
        }

        protected override void OnMouseDragMove(int x, int y)
        {

            //If they are trying to draw in a cell that already has a control, then we
            //do not want to draw an outline
            if (droppedCellPosition == InvalidPoint)
            {
                Cursor.Current = Cursors.No;
                return;
            }

            base.OnMouseDragMove(x, y);
        }

        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.OnMouseDragEnd"]/*' />
        /// <devdoc>
        ///     If the user has drawn a rectangle with a toolbox item,
        ///     we'll cache off the cell in which it happened so we can
        ///     add it @ the right location in the event OnChildAdded.
        /// </devdoc>
        protected override void OnMouseDragEnd(bool cancel)
        {

            if (droppedCellPosition == InvalidPoint)
            {
                // If they are trying to draw in a cell that already has a control, then just act like a cancel
                cancel = true;
            }

            base.OnMouseDragEnd(cancel);
        }

        private void OnRowColMenuOpening(object sender, CancelEventArgs e)
        {
            e.Cancel = false;
            //Set the size mode correctly
            ToolStripDropDownMenu menu = sender as ToolStripDropDownMenu;
            if (menu != null)
            {
                int selCount = 0;
                ISelectionService selSvc = GetService(typeof(ISelectionService)) as ISelectionService;
                if (selSvc != null)
                {
                    selCount = selSvc.SelectionCount;
                }

                // Always make sure and set the Enabled state in case the user
                // has changed the selection since the last time the menu was shown.
                bool enabled = (selCount == 1) && (InheritanceAttribute != InheritanceAttribute.InheritedReadOnly);

                menu.Items["add"].Enabled = enabled;
                menu.Items["insert"].Enabled = enabled;
                menu.Items["delete"].Enabled = enabled;
                menu.Items["sizemode"].Enabled = enabled;
                menu.Items["absolute"].Enabled = enabled;
                menu.Items["percent"].Enabled = enabled;
                menu.Items["autosize"].Enabled = enabled;

                if (selCount == 1)
                {
                    ((ToolStripMenuItem)menu.Items["absolute"]).Checked = false;
                    ((ToolStripMenuItem)menu.Items["percent"]).Checked = false;
                    ((ToolStripMenuItem)menu.Items["autosize"]).Checked = false;

                    bool isRow = (bool)menu.Tag;
                    switch (isRow ? Table.RowStyles[curRow].SizeType : Table.ColumnStyles[curCol].SizeType)
                    {
                        case SizeType.Absolute:
                            ((ToolStripMenuItem)menu.Items["absolute"]).Checked = true;
                            break;
                        case SizeType.Percent:
                            ((ToolStripMenuItem)menu.Items["percent"]).Checked = true;
                            break;
                        case SizeType.AutoSize:
                            ((ToolStripMenuItem)menu.Items["autosize"]).Checked = true;
                            break;
                        default:
                            Debug.Fail("Unknown SizeType!");
                            break;
                    }

                    if ((isRow ? Table.RowCount : Table.ColumnCount) < 2)
                    {
                        //can't remove a row/column if we only have
                        menu.Items["delete"].Enabled = false;
                    }
                }
            }
        }

        /// <devdoc>
        ///     Adds a row/col to the end of the table
        /// </devdoc>
        private void OnAdd(bool isRow)
        {

            // get the property and add to it...
            IDesignerHost host = GetService(typeof(IDesignerHost)) as IDesignerHost;
            if (host != null && Table.Site != null)
            {
                using (DesignerTransaction t = host.CreateTransaction(SR.GetString(isRow ? SR.TableLayoutPanelDesignerAddRowUndoUnit :
                                                                                            SR.TableLayoutPanelDesignerAddColumnUndoUnit, Table.Site.Name)))
                {
                    try
                    {
                        Table.SuspendLayout(); // To avoid flickering
                        // This ensures that the Row/Col Style gets set BEFORE the row is added. This in turn
                        // ensures that the row/col shows up. Since we turn off tablelayout, a style won't have been added
                        // when EnsureVisibleStyles is called from the shadowed property.
                        InsertRowCol(isRow, isRow ? Table.RowCount : Table.ColumnCount);
                        Table.ResumeLayout();
                        t.Commit();
                    }
                    catch (CheckoutException checkoutException)
                    {
                        if (CheckoutException.Canceled.Equals(checkoutException))
                        {
                            if (t != null)
                            {
                                t.Cancel();
                            }
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
            }

        }

        /// <devdoc>
        ///     Adds a row/col to the end of the table
        /// </devdoc>
        private void OnAddClick(object sender, EventArgs e)
        {
            //Tag = isRow
            OnAdd((bool)((ToolStripMenuItem)sender).Tag);
        }

        /// <devdoc>
        ///     Adds an row or column at the specified index.
        ///     This method is also used by the StyleCollectionEditor, so any transaction must
        ///     be managed by the caller.
        /// </devdoc>
        internal void InsertRowCol(bool isRow, int index)
        {

            // We shadow the ColumnCount/RowCount property, so let's add the style first
            // to make sure that the right style is added at the right location.

            try
            {
                if (isRow)
                {
                    PropertyDescriptor rowProp = TypeDescriptor.GetProperties(Table)["RowCount"];
                    if (rowProp != null)
                    {

                        PropChanging(rowStyleProp);
                        Table.RowStyles.Insert(index, new RowStyle(SizeType.Absolute, DesignerUtils.MINIMUMSTYLESIZE));
                        PropChanged(rowStyleProp);

                        rowProp.SetValue(Table, Table.RowCount + 1);
                    }
                }
                else
                {
                    PropertyDescriptor colProp = TypeDescriptor.GetProperties(Table)["ColumnCount"];
                    if (colProp != null)
                    {

                        PropChanging(colStyleProp);
                        Table.ColumnStyles.Insert(index, new ColumnStyle(SizeType.Absolute, DesignerUtils.MINIMUMSTYLESIZE));
                        PropChanged(colStyleProp);

                        colProp.SetValue(Table, Table.ColumnCount + 1);
                    }
                }
            }
            catch (System.InvalidOperationException ex)
            {
                IUIService uiService = (IUIService)GetService(typeof(IUIService));
                uiService.ShowError(ex.Message);
            }
            BehaviorService.Invalidate(BehaviorService.ControlRectInAdornerWindow(Control));

        }

        /// <devdov>
        ///     When we insert a row/col, we need to fix up any control that spans into the selected row/col to include the newly inserted row/col.
        ///     We also need to move controls after the original row/col into the new row/col.
        ///     index is the index of the row/col we just inserted
        ///     Also used by the StyleCollectionEditor
        /// </devdoc>
        internal void FixUpControlsOnInsert(bool isRow, int index)
        {

            PropertyDescriptor childProp = TypeDescriptor.GetProperties(Table)["Controls"];
            PropChanging(childProp);

            foreach (Control child in Table.Controls)
            {

                int currentIndex = isRow ? Table.GetRow(child) : Table.GetColumn(child);
                PropertyDescriptor prop = TypeDescriptor.GetProperties(child)[isRow ? "Row" : "Column"];
                PropertyDescriptor spanProp = TypeDescriptor.GetProperties(child)[isRow ? "RowSpan" : "ColumnSpan"];

                if (currentIndex == -1)
                {
                    //this is a flow element. We don't really know where
                    //this is going to go, so we cannot fix up anything.
                    continue;
                }

                //push all controls >= the original row/col into the new row/col
                if (currentIndex >= index)
                {
                    if (prop != null)
                    {
                        prop.SetValue(child, currentIndex + 1);
                    }
                }
                else
                {
                    //If the control is before the row/col we are inserting and the control has a span that includes the inserted row/col
                    //the increase the span to include the insert row/col
                    int span = isRow ? Table.GetRowSpan(child) : Table.GetColumnSpan(child); //span is always at least 1
                    if (currentIndex + span > index)
                    {
                        if (spanProp != null)
                        {
                            spanProp.SetValue(child, span + 1);
                        }
                    }
                }
            }

            PropChanged(childProp);

        }


        private void OnInsertClick(object sender, EventArgs e)
        {


            IDesignerHost host = GetService(typeof(IDesignerHost)) as IDesignerHost;
            if (host != null && Table.Site != null)
            {
                bool isRow = (bool)((ToolStripMenuItem)sender).Tag;
                using (DesignerTransaction t = host.CreateTransaction(SR.GetString(isRow ? SR.TableLayoutPanelDesignerAddRowUndoUnit :
                                                                                            SR.TableLayoutPanelDesignerAddColumnUndoUnit, Table.Site.Name)))
                {
                    try
                    {
                        Table.SuspendLayout();
                        InsertRowCol(isRow, isRow ? curRow : curCol);
                        FixUpControlsOnInsert(isRow, isRow ? curRow : curCol);
                        Table.ResumeLayout();
                        t.Commit();
                    }
                    catch (CheckoutException checkoutException)
                    {
                        if (CheckoutException.Canceled.Equals(checkoutException))
                        {
                            if (t != null)
                            {
                                t.Cancel();
                            }
                        }
                        else
                        {
                            throw;
                        }
                    }
                    catch (System.InvalidOperationException ex)
                    {
                        IUIService uiService = (IUIService)GetService(typeof(IUIService));
                        uiService.ShowError(ex.Message);
                    }
                }
            }

        }

        /// <devdoc>
        ///     When we remove a row/col, we need to fix up any control that spans into the selected row/col.
        ///     We also need to adjust controls after the original row/col.
        ///     index is the index of the row/col we are about to delete
        ///
        ///     deleteList will hold a list of controls, which has the same index as the row/col, we are
        ///     deleting. They are actually not deleted in this method because the method is also called,
        ///     by the StyleCollectionEditor which does not want to delete controls right away.
        /// </devdoc>
        internal void FixUpControlsOnDelete(bool isRow, int index, ArrayList deleteList)
        {

            PropertyDescriptor childProp = TypeDescriptor.GetProperties(Table)["Controls"];
            PropChanging(childProp);

            foreach (Control child in Table.Controls)
            {
                int currentIndex = isRow ? Table.GetRow(child) : Table.GetColumn(child);
                PropertyDescriptor prop = TypeDescriptor.GetProperties(child)[isRow ? "Row" : "Column"];
                PropertyDescriptor spanProp = TypeDescriptor.GetProperties(child)[isRow ? "RowSpan" : "ColumnSpan"];

                if (currentIndex == index)
                {
                    //We add the deleteList.Contains check just to make extra sure. Could be
                    //that the deleteList for some reason already contained the child.
                    if (!deleteList.Contains(child))
                    {
                        deleteList.Add(child);
                    }
                    continue;
                }

                if (currentIndex == -1 || deleteList.Contains(child))
                {
                    //If this is a flow element. We don't really know where this is going to go, so we cannot fix up anything.
                    //If the child has already been marked for deletion, we can keep going
                    continue;
                }

                Debug.Assert(currentIndex != index);

                //push all controls >= the original row/col into the new row/col, but only
                if (currentIndex > index)
                {
                    if (prop != null)
                    {
                        prop.SetValue(child, currentIndex - 1);
                    }
                }
                else
                {
                    //If the control is before the row/col we are removing and the control has a span that includes the row/col
                    //we are deleting, then decrease the span.
                    int span = isRow ? Table.GetRowSpan(child) : Table.GetColumnSpan(child); //span is always at least 1
                    if (currentIndex + span > index)
                    {
                        if (spanProp != null)
                        {
                            //We've bled into the row/col, shrink up as expected
                            spanProp.SetValue(child, span - 1);
                        }
                    }
                }
            }

            PropChanged(childProp);

        }


        // Also used by the StyleCollectionEditor
        internal void DeleteRowCol(bool isRow, int index)
        {

            if (isRow)
            {
                PropertyDescriptor rowProp = TypeDescriptor.GetProperties(Table)["RowCount"];
                if (rowProp != null)
                {
                    rowProp.SetValue(Table, Table.RowCount - 1);

                    PropChanging(rowStyleProp);
                    Table.RowStyles.RemoveAt(index);
                    PropChanged(rowStyleProp);
                }
            }
            else
            {
                PropertyDescriptor colProp = TypeDescriptor.GetProperties(Table)["ColumnCount"];
                if (colProp != null)
                {
                    colProp.SetValue(Table, Table.ColumnCount - 1);

                    PropChanging(colStyleProp);
                    Table.ColumnStyles.RemoveAt(index);
                    PropChanged(colStyleProp);
                }
            }

        }

        private void OnRemoveInternal(bool isRow, int index)
        {

            if ((isRow ? Table.RowCount : Table.ColumnCount) < 2)
            {
                //can't remove a row/column if we only have 1
                return;
            }

            IDesignerHost host = GetService(typeof(IDesignerHost)) as IDesignerHost;
            if (host != null && Table.Site != null)
            {
                using (DesignerTransaction t = host.CreateTransaction(SR.GetString(isRow ? SR.TableLayoutPanelDesignerRemoveRowUndoUnit :
                                                                        SR.TableLayoutPanelDesignerRemoveColumnUndoUnit, Table.Site.Name)))
                {
                    try
                    {
                        Table.SuspendLayout();
                        ArrayList deleteList = new ArrayList();

                        //First fix up any controls in the row/col we are deleting
                        FixUpControlsOnDelete(isRow, index, deleteList);
                        //Then delete the row col
                        DeleteRowCol(isRow, index);

                        //Now delete any child control

                        // IF YOU CHANGE THIS, YOU SHOULD ALSO CHANGE THE CODE IN StyleCollectionEditor.OnOkButtonClick
                        if (deleteList.Count > 0)
                        {
                            PropertyDescriptor childProp = TypeDescriptor.GetProperties(Table)["Controls"];
                            PropChanging(childProp);
                            foreach (object o in deleteList)
                            {
                                ArrayList al = new ArrayList();
                                DesignerUtils.GetAssociatedComponents((IComponent)o, host, al);
                                foreach (IComponent comp in al)
                                {
                                    compSvc.OnComponentChanging(comp, null);
                                }
                                host.DestroyComponent(o as Component);
                            }
                            PropChanged(childProp);
                        }

                        Table.ResumeLayout();
                        t.Commit();
                    }
                    catch (CheckoutException checkoutException)
                    {
                        if (CheckoutException.Canceled.Equals(checkoutException))
                        {
                            if (t != null)
                            {
                                t.Cancel();
                            }
                        }
                        else
                        {
                            throw;
                        }
                    }
                }

            }

        }

        /// <devdoc>
        ///     Always removes the last row/col
        /// </devdoc>
        private void OnRemove(bool isRow)
        {
            OnRemoveInternal(isRow, isRow ? Table.RowCount - 1 : Table.ColumnCount - 1);
        }

        /// <devdoc>
        ///     Removes a rol/col at a specific index.
        /// </devdoc>
        private void OnDeleteClick(object sender, EventArgs e)
        {
            try
            {
                bool isRow = (bool)((ToolStripMenuItem)sender).Tag;
                OnRemoveInternal(isRow, isRow ? curRow : curCol);
            }
            catch (System.InvalidOperationException ex)
            {
                IUIService uiService = (IUIService)GetService(typeof(IUIService));
                uiService.ShowError(ex.Message);
            }
        }

        private void ChangeSizeType(bool isRow, SizeType newType)
        {
            TableLayoutStyleCollection styles = null;
            try
            {
                if (isRow)
                {
                    styles = Table.RowStyles;
                }
                else
                {
                    styles = Table.ColumnStyles;
                }

                int index = isRow ? curRow : curCol;

                if (styles[index].SizeType == newType)
                {
                    // nuthin' to do
                    return;
                }

                int[] rh = Table.GetRowHeights();
                int[] ch = Table.GetColumnWidths();

                if ((isRow && rh.Length < index - 1) || (!isRow && ch.Length < index - 1))
                {
                    //something got messed up
                    Debug.Fail("Our indices are outta whack, how did that happen?");
                    return;
                }

                IDesignerHost host = GetService(typeof(IDesignerHost)) as IDesignerHost;
                if (host != null && Table.Site != null)
                {
                    using (DesignerTransaction t = host.CreateTransaction(SR.GetString(SR.TableLayoutPanelDesignerChangeSizeTypeUndoUnit, Table.Site.Name)))
                    {
                        try
                        {
                            Table.SuspendLayout();

                            PropChanging(isRow ? rowStyleProp : colStyleProp);

                            switch (newType)
                            {
                                case SizeType.Absolute:
                                    styles[index].SizeType = SizeType.Absolute;
                                    if (isRow)
                                    {
                                        Table.RowStyles[index].Height = rh[index];
                                    }
                                    else
                                    {
                                        Table.ColumnStyles[index].Width = ch[index];
                                    }
                                    break;
                                case SizeType.Percent:
                                    styles[index].SizeType = SizeType.Percent;
                                    if (isRow)
                                    {
                                        Table.RowStyles[index].Height = DesignerUtils.MINIMUMSTYLEPERCENT;
                                    }
                                    else
                                    {
                                        Table.ColumnStyles[index].Width = DesignerUtils.MINIMUMSTYLEPERCENT;
                                    }
                                    break;
                                case SizeType.AutoSize:
                                    styles[index].SizeType = SizeType.AutoSize;
                                    break;
                                default:
                                    Debug.Fail("Unknown SizeType!");
                                    break;
                            }

                            PropChanged(isRow ? rowStyleProp : colStyleProp);

                            Table.ResumeLayout();
                            t.Commit();
                        }
                        catch (CheckoutException checkoutException)
                        {
                            if (CheckoutException.Canceled.Equals(checkoutException))
                            {
                                if (t != null)
                                {
                                    t.Cancel();
                                }
                            }
                            else
                            {
                                throw;
                            }
                        }
                    }
                }
            }
            catch (System.InvalidOperationException ex)
            {
                IUIService uiService = (IUIService)GetService(typeof(IUIService));
                uiService.ShowError(ex.Message);
            }
        }

        private void OnAbsoluteClick(object sender, EventArgs e)
        {
            ChangeSizeType((bool)((ToolStripMenuItem)sender).Tag, SizeType.Absolute);
        }

        private void OnPercentClick(object sender, EventArgs e)
        {
            ChangeSizeType((bool)((ToolStripMenuItem)sender).Tag, SizeType.Percent);
        }

        private void OnAutoSizeClick(object sender, EventArgs e)
        {
            ChangeSizeType((bool)((ToolStripMenuItem)sender).Tag, SizeType.AutoSize);
        }

        private void OnEdit()
        {
            try
            {
                EditorServiceContext.EditValue(this, Table, "ColumnStyles");
            }
            catch (System.InvalidOperationException ex)
            {
                IUIService uiService = (IUIService)GetService(typeof(IUIService));
                uiService.ShowError(ex.Message);
            }
        }

        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.OnVerbRemove"]/*' />
        /// <devdoc>
        ///     NOTICE!!! The Regex is copied from CompMod\System\ComponentModel\Design\DesignerVerb.cs constructor.
        ///               When you changed this regex, please change it correspondingly in DesignerVerg.cs.
        /// </devdoc>
        private string ReplaceText(string text)
        {
            return text == null ? null : Regex.Replace(text, @"\(\&.\)", "");
        }

        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.OnVerbRemove"]/*' />
        /// <devdoc>
        ///     This is our callback for designer verbs: remove row and remove column.
        ///     All we do here is increment or decrememnt out col/row count.
        /// </devdoc>
        private void OnVerbRemove(object sender, EventArgs e)
        {

            //sniff the text of the verb to see if we're adding columns or rows
            bool isRow = ((DesignerVerb)sender).Text.Equals(ReplaceText(SR.GetString(SR.TableLayoutPanelDesignerRemoveRow)));
            OnRemove(isRow);
        }

        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.OnVerbAdd"]/*' />
        /// <devdoc>
        ///     This is our callback for designer verbs: add row and add column.
        /// </devdoc>
        private void OnVerbAdd(object sender, EventArgs e)
        {

            //sniff the text of the verb to see if we're adding columns or rows
            bool isRow = ((DesignerVerb)sender).Text.Equals(ReplaceText(SR.GetString(SR.TableLayoutPanelDesignerAddRow)));
            OnAdd(isRow);

        }

        private void OnVerbEdit(object sender, EventArgs e)
        {
            OnEdit();
        }


        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.PreFilterProperties"]/*' />
        /// <devdoc>
        /// </devdoc>
        protected override void PreFilterProperties(IDictionary properties)
        {

            base.PreFilterProperties(properties);

            // Handle shadowed properties
            string[] shadowProps = new string[] {
                "ColumnStyles",
                "RowStyles",
                "ColumnCount",
                "RowCount"
            };

            // VSWhidbey 491088
            // To enable the PropertyGrid to work with the TableLayoutPanel at runtime (when no designer is available),
            // the above properties are marked browsable(false) and re-enabled when a designer is present.
            // Since so much of the logic for keeping the TLP in a valid Row/Column state is designer dependent, 
            // these properties are not accessible by the PropertyGrid without a designer.
            Attribute[] attribs = new Attribute[] { new BrowsableAttribute(true) };

            for (int i = 0; i < shadowProps.Length; i++)
            {
                PropertyDescriptor prop = (PropertyDescriptor)properties[shadowProps[i]];
                if (prop != null)
                {
                    properties[shadowProps[i]] = TypeDescriptor.CreateProperty(typeof(TableLayoutPanelDesigner), prop, attribs);
                }
            }

            // replace this one seperately because it is of a different type (DesignerTableLayoutControlCollection) than
            // the original property (TableLayoutControlCollection)
            //
            PropertyDescriptor controlsProp = (PropertyDescriptor)properties["Controls"];

            if (controlsProp != null)
            {
                Attribute[] attrs = new Attribute[controlsProp.Attributes.Count];
                controlsProp.Attributes.CopyTo(attrs, 0);
                properties["Controls"] = TypeDescriptor.CreateProperty(typeof(TableLayoutPanelDesigner), "Controls", typeof(DesignerTableLayoutControlCollection), attrs);
            }
        }


        /// <include file='doc\TableLayoutPanelDesigner.uex' path='docs/doc[@for="TableLayoutPanelDesigner.Refresh"]/*' />
        /// <devdoc>
        ///     Called after we modify the table - sync the selection and repaint.
        /// </devdoc>
        private void Refresh()
        {
            //refresh selection, glyphs, and adorners
            BehaviorService.SyncSelection();

            if (Table != null)
            {
                Table.Invalidate(true);
            }
        }

        private void PropChanging(PropertyDescriptor prop)
        {
            if (compSvc != null && prop != null)
            {
                compSvc.OnComponentChanging(Table, prop);
            }
        }


        private void PropChanged(PropertyDescriptor prop)
        {
            if (compSvc != null && prop != null)
            {
                compSvc.OnComponentChanged(Table, prop, null, null);
            }
        }

        [ListBindable(false)]
        [DesignerSerializer(typeof(DesignerTableLayoutControlCollectionCodeDomSerializer), typeof(CodeDomSerializer))]
        internal class DesignerTableLayoutControlCollection : TableLayoutControlCollection, IList
        {

            TableLayoutControlCollection realCollection;

            public DesignerTableLayoutControlCollection(TableLayoutPanel owner) : base(owner)
            {
                this.realCollection = owner.Controls;
            }

            /// <include file='doc\Control.uex' path='docs/doc[@for="Control.ControlCollection.Count"]/*' />
            /// <devdoc>
            ///     Retrieves the number of child controls.
            /// </devdoc>
            public override int Count
            {
                get
                {
                    return realCollection.Count;
                }
            }

            /// <include file='doc\Control.uex' path='docs/doc[@for="ControlCollection.ICollection.SyncRoot"]/*' />
            /// <internalonly/>
            object ICollection.SyncRoot
            {
                get
                {
                    return this;
                }
            }

            /// <include file='doc\Control.uex' path='docs/doc[@for="ControlCollection.ICollection.IsSynchronized"]/*' />
            /// <internalonly/>
            bool ICollection.IsSynchronized
            {
                get
                {
                    return false;
                }
            }

            /// <include file='doc\Control.uex' path='docs/doc[@for="ControlCollection.IList.IsFixedSize"]/*' />
            /// <internalonly/>
            bool IList.IsFixedSize
            {
                get
                {
                    return false;
                }
            }

            /// <include file='doc\Control.uex' path='docs/doc[@for="Control.ControlCollection.IsReadOnly"]/*' />
            /// <devdoc>
            ///    <para>[To be supplied.]</para>
            /// </devdoc>
            public new bool IsReadOnly
            {
                get
                {
                    return realCollection.IsReadOnly;
                }
            }


            /// <include file='doc\Control.uex' path='docs/doc[@for="ControlCollection.IList.Add"]/*' />
            /// <internalonly/>
            int IList.Add(object control)
            {
                return ((IList)realCollection).Add(control);
            }

            public override void Add(Control c)
            {
                realCollection.Add(c);
            }

            /// <include file='doc\Control.uex' path='docs/doc[@for="Control.ControlCollection.AddRange"]/*' />
            /// <devdoc>
            ///    <para>[To be supplied.]</para>
            /// </devdoc>
            public override void AddRange(Control[] controls)
            {
                realCollection.AddRange(controls);
            }

            /// <include file='doc\Control.uex' path='docs/doc[@for="ControlCollection.IList.Contains"]/*' />
            /// <internalonly/>
            bool IList.Contains(object control)
            {
                return ((IList)realCollection).Contains(control);
            }

            public new void CopyTo(Array dest, int index)
            {
                realCollection.CopyTo(dest, index);
            }

            /// <include file='doc\Control.uex' path='docs/doc[@for="Control.ControlCollection.Equals"]/*' />
            /// <internalonly/>
            public override bool Equals(object other)
            {
                return realCollection.Equals(other);
            }

            public new IEnumerator GetEnumerator()
            {
                return realCollection.GetEnumerator();
            }

            /// <include file='doc\Control.uex' path='docs/doc[@for="Control.ControlCollection.GetHashCode"]/*' />
            /// <internalonly/>
            public override int GetHashCode()
            {
                return realCollection.GetHashCode();
            }

            /// <include file='doc\Control.uex' path='docs/doc[@for="ControlCollection.IList.IndexOf"]/*' />
            /// <internalonly/>
            int IList.IndexOf(object control)
            {
                return ((IList)realCollection).IndexOf(control);
            }

            /// <include file='doc\Control.uex' path='docs/doc[@for="ControlCollection.IList.Insert"]/*' />
            /// <internalonly/>
            void IList.Insert(int index, object value)
            {
                ((IList)realCollection).Insert(index, value);
            }

            /// <include file='doc\Control.uex' path='docs/doc[@for="ControlCollection.IList.Remove"]/*' />
            /// <internalonly/>
            void IList.Remove(object control)
            {
                ((IList)realCollection).Remove(control);
            }

            /// <include file='doc\Control.uex' path='docs/doc[@for="ControlCollection.IList.Remove"]/*' />
            /// <internalonly/>
            void IList.RemoveAt(int index)
            {
                ((IList)realCollection).RemoveAt(index);
            }

            /// <include file='doc\Control.uex' path='docs/doc[@for="ControlCollection.IList.this"]/*' />
            /// <internalonly/>
            object IList.this[int index]
            {
                get
                {
                    return ((IList)realCollection)[index];
                }
                set
                {
                    throw new NotSupportedException();
                }
            }

            public override void Add(Control control, int column, int row)
            {
                realCollection.Add(control, column, row);
            }

            public override int GetChildIndex(Control child, bool throwException)
            {
                return realCollection.GetChildIndex(child, throwException);
            }

            // we also need to redirect this guy 
            public override void SetChildIndex(Control child, int newIndex)
            {
                realCollection.SetChildIndex(child, newIndex);
            }

            /// <include file='doc\Control.uex' path='docs/doc[@for="Control.ControlCollection.Clear"]/*' />
            /// <devdoc>
            ///    <para>[To be supplied.]</para>
            /// </devdoc>
            public override void Clear()
            {

                // only remove the sited non-inherited components
                //
                for (int i = realCollection.Count - 1; i >= 0; i--)
                {
                    if (realCollection[i] != null &&
                        realCollection[i].Site != null &&
                        TypeDescriptor.GetAttributes(realCollection[i]).Contains(InheritanceAttribute.NotInherited))
                    {
                        realCollection.RemoveAt(i);
                    }
                }
            }
        }

        // Custom code dom serializer for the DesignerControlCollection. We need this so we can filter out controls
        // that aren't sited in the host's container.
        internal class DesignerTableLayoutControlCollectionCodeDomSerializer : TableLayoutControlCollectionCodeDomSerializer
        {
            protected override object SerializeCollection(IDesignerSerializationManager manager, CodeExpression targetExpression, Type targetType, ICollection originalCollection, ICollection valuesToSerialize)
            {
                ArrayList subset = new ArrayList();

                if (valuesToSerialize != null && valuesToSerialize.Count > 0)
                {
                    foreach (object val in valuesToSerialize)
                    {
                        IComponent comp = val as IComponent;

                        if (comp != null && comp.Site != null && !(comp.Site is INestedSite))
                        {
                            subset.Add(comp);
                        }
                    }
                }

                return base.SerializeCollection(manager, targetExpression, targetType, originalCollection, subset);
            }
        }
    }
}
