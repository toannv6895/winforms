// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;

namespace System.Windows.Forms.Design
{
    /// <include file='doc\StyleCollectionEditor.uex' path='docs/doc[@for="StyleCollectionEditor"]/*' />
    /// <devdoc>
    ///      This collection editor is used to add value to the TableLayoutPanel's
    ///      'Style' collections: RowStyle and ColumnStyle.  Here, we override the
    ///      CreateInstance method and set the width or height of the new style
    ///      to a default minimum size.
    /// </devdoc>
    internal class StyleCollectionEditor : CollectionEditor
    {
        private bool isRowCollection = false;

        protected string helptopic;

        /// <include file='doc\StyleCollectionEditor.uex' path='docs/doc[@for="StyleCollectionEditor.StyleCollectionEditor"]/*' />
        /// <devdoc>
        ///    Standard public constructor.
        /// </devdoc>
        public StyleCollectionEditor(Type type) : base(type)
        {
            isRowCollection = type.IsAssignableFrom(typeof(TableLayoutRowStyleCollection));
        }

        /// <summary>
        /// Overridden to create our editor form instead of the standard collection editor form.
        /// </summary>
        /// <returns>An instance of a StyleEditorForm</returns>
        protected override CollectionEditor.CollectionForm CreateCollectionForm()
        {
            return new StyleEditorForm(this, isRowCollection);
        }

        /// <include file='doc\StyleCollectionEditor.uex' path='docs/doc[@for="StyleCollectionEditor.HelpTopic"]/*' />
        /// <devdoc>
        ///    <para>Gets the help topic to display for the dialog help button or pressing F1. Override to
        ///          display a different help topic.</para>
        /// </devdoc>
        protected override string HelpTopic
        {
            get
            {
                return helptopic;
            }
        }

        protected class NavigationalTableLayoutPanel : TableLayoutPanel
        {

            private List<RadioButton> RadioButtons
            {
                get
                {
                    List<RadioButton> radioButtons = new List<RadioButton>();
                    foreach (Control c in Controls)
                    {
                        RadioButton rb = c as RadioButton;
                        if (rb != null)
                        {
                            radioButtons.Add(rb);
                        }
                    }
                    return radioButtons;
                }
            }

            protected override bool ProcessDialogKey(Keys keyData)
            {
                bool down = keyData == Keys.Down;
                bool up = keyData == Keys.Up;

                if (down || up)
                {
                    List<RadioButton> radioButtons = RadioButtons;

                    for (int i = 0; i < radioButtons.Count; i++)
                    {
                        RadioButton rb = radioButtons[i];
                        if (rb.Focused)
                        {
                            int focusIndex;
                            if (down)
                            {
                                focusIndex = i == RadioButtons.Count - 1 ? 0 : i + 1;
                            }
                            else
                            {
                                focusIndex = i == 0 ? RadioButtons.Count - 1 : i - 1;
                            }
                            radioButtons[focusIndex].Focus();
                            return true;
                        }
                    }
                }

                return base.ProcessDialogKey(keyData);
            }
        }

        protected class StyleEditorForm : CollectionEditor.CollectionForm
        {
            private StyleCollectionEditor editor = null;
            private bool isRowCollection = false;
            private TableLayoutPanel tlp = null;
            private TableLayoutPanelDesigner tlpDesigner = null;
            IComponentChangeService compSvc = null;
            private ArrayList deleteList = null;

            private bool isDialogDirty = false;

            private bool haveInvoked = false;

            //listview subitem indices

            private static int MEMBER_INDEX = 0;
            private static int TYPE_INDEX = 1;
            private static int VALUE_INDEX = 2;

            private PropertyDescriptor rowStyleProp;
            private PropertyDescriptor colStyleProp;

            /// <summary>
            /// All our control instance variables.
            /// </summary>

            private System.Windows.Forms.TableLayoutPanel overarchingTableLayoutPanel;

            private System.Windows.Forms.TableLayoutPanel addRemoveInsertTableLayoutPanel;
            private System.Windows.Forms.Button addButton;
            private System.Windows.Forms.Button removeButton;
            private System.Windows.Forms.Button insertButton;

            private System.Windows.Forms.TableLayoutPanel okCancelTableLayoutPanel;
            private System.Windows.Forms.Button okButton;
            private System.Windows.Forms.Button cancelButton;

            private System.Windows.Forms.Label memberTypeLabel;
            private System.Windows.Forms.ComboBox columnsOrRowsComboBox;

            private System.Windows.Forms.GroupBox sizeTypeGroupBox;
            private System.Windows.Forms.RadioButton absoluteRadioButton;
            private System.Windows.Forms.RadioButton percentRadioButton;
            private System.Windows.Forms.RadioButton autoSizedRadioButton;

            private NavigationalTableLayoutPanel sizeTypeTableLayoutPanel;
            private System.Windows.Forms.Label pixelsLabel;
            private System.Windows.Forms.NumericUpDown absoluteNumericUpDown;
            private System.Windows.Forms.Label percentLabel;
            private System.Windows.Forms.NumericUpDown percentNumericUpDown;

            private System.Windows.Forms.ListView columnsAndRowsListView;
            private System.Windows.Forms.ColumnHeader membersColumnHeader;
            private System.Windows.Forms.ColumnHeader sizeTypeColumnHeader;
            private TableLayoutPanel helperTextTableLayoutPanel;
            private PictureBox infoPictureBox1;
            private PictureBox infoPictureBox2;
            private LinkLabel helperLinkLabel1;
            private LinkLabel helperLinkLabel2;
            private TableLayoutPanel showTableLayoutPanel;
            private System.Windows.Forms.ColumnHeader valueColumnHeader;

            private const int UpDownLeftMargin = 10;
            private int scaledUpDownLeftMargin = UpDownLeftMargin;

            private const int UpDownTopMargin = 4;
            private int scaledUpDownTopMargin = UpDownTopMargin;

            private const int LabelRightMargin = 5;
            private int scaledLabelRightMargin = LabelRightMargin;

            internal StyleEditorForm(CollectionEditor editor, bool isRowCollection) : base(editor)
            {
                this.editor = (StyleCollectionEditor)editor;
                this.isRowCollection = isRowCollection;
                InitializeComponent();
                HookEvents();

                // Enable Vista explorer list view style
                DesignerUtils.ApplyListViewThemeStyles(columnsAndRowsListView);

                this.ActiveControl = columnsAndRowsListView;
                tlp = Context.Instance as TableLayoutPanel;
                tlp.SuspendLayout();

                deleteList = new ArrayList();

                // Get the designer associated with the TLP
                IDesignerHost host = tlp.Site.GetService(typeof(IDesignerHost)) as IDesignerHost;
                if (host != null)
                {
                    tlpDesigner = host.GetDesigner((IComponent)tlp) as TableLayoutPanelDesigner;
                    compSvc = host.GetService(typeof(IComponentChangeService)) as IComponentChangeService;
                }

                rowStyleProp = TypeDescriptor.GetProperties(tlp)["RowStyles"];
                colStyleProp = TypeDescriptor.GetProperties(tlp)["ColumnStyles"];

                tlpDesigner.SuspendEnsureAvailableStyles();
            }

            private void HookEvents()
            {
                this.HelpButtonClicked += new System.ComponentModel.CancelEventHandler(this.OnHelpButtonClicked);

                this.columnsAndRowsListView.SelectedIndexChanged += new System.EventHandler(this.OnListViewSelectedIndexChanged);

                this.columnsOrRowsComboBox.SelectionChangeCommitted += new System.EventHandler(this.OnComboBoxSelectionChangeCommitted);

                this.okButton.Click += new System.EventHandler(this.OnOkButtonClick);
                this.cancelButton.Click += new System.EventHandler(this.OnCancelButtonClick);

                this.addButton.Click += new System.EventHandler(this.OnAddButtonClick);
                this.removeButton.Click += new System.EventHandler(this.OnRemoveButtonClick);
                this.insertButton.Click += new System.EventHandler(this.OnInsertButtonClick);

                this.absoluteRadioButton.Enter += new System.EventHandler(this.OnAbsoluteEnter);
                this.absoluteNumericUpDown.ValueChanged += new System.EventHandler(this.OnValueChanged);

                this.percentRadioButton.Enter += new System.EventHandler(this.OnPercentEnter);
                this.percentNumericUpDown.ValueChanged += new System.EventHandler(this.OnValueChanged);

                this.autoSizedRadioButton.Enter += new System.EventHandler(this.OnAutoSizeEnter);

                this.Shown += new System.EventHandler(this.OnShown);

                this.helperLinkLabel1.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.OnLink1Click);
                this.helperLinkLabel2.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.OnLink2Click);
            }

            #region Windows Form Designer generated code
            /// <summary>
            /// Required method for Designer support - do not modify
            /// the contents of this method with the code editor.
            /// </summary>
            private void InitializeComponent()
            {
                System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(StyleCollectionEditor));
                this.addRemoveInsertTableLayoutPanel = new System.Windows.Forms.TableLayoutPanel();
                this.addButton = new System.Windows.Forms.Button();
                this.removeButton = new System.Windows.Forms.Button();
                this.insertButton = new System.Windows.Forms.Button();
                this.okCancelTableLayoutPanel = new System.Windows.Forms.TableLayoutPanel();
                this.okButton = new System.Windows.Forms.Button();
                this.cancelButton = new System.Windows.Forms.Button();
                this.overarchingTableLayoutPanel = new System.Windows.Forms.TableLayoutPanel();
                this.showTableLayoutPanel = new System.Windows.Forms.TableLayoutPanel();
                this.memberTypeLabel = new System.Windows.Forms.Label();
                this.columnsOrRowsComboBox = new System.Windows.Forms.ComboBox();
                this.columnsAndRowsListView = new System.Windows.Forms.ListView();
                this.membersColumnHeader = new System.Windows.Forms.ColumnHeader(resources.GetString("columnsAndRowsListView.Columns"));
                this.sizeTypeColumnHeader = new System.Windows.Forms.ColumnHeader(resources.GetString("columnsAndRowsListView.Columns1"));
                this.valueColumnHeader = new System.Windows.Forms.ColumnHeader(resources.GetString("columnsAndRowsListView.Columns2"));
                this.helperTextTableLayoutPanel = new System.Windows.Forms.TableLayoutPanel();
                this.infoPictureBox2 = new System.Windows.Forms.PictureBox();
                this.infoPictureBox1 = new System.Windows.Forms.PictureBox();
                this.helperLinkLabel1 = new System.Windows.Forms.LinkLabel();
                this.helperLinkLabel2 = new System.Windows.Forms.LinkLabel();
                this.sizeTypeGroupBox = new System.Windows.Forms.GroupBox();
                this.sizeTypeTableLayoutPanel = new NavigationalTableLayoutPanel();
                this.absoluteNumericUpDown = new System.Windows.Forms.NumericUpDown();
                this.absoluteRadioButton = new System.Windows.Forms.RadioButton();
                this.pixelsLabel = new System.Windows.Forms.Label();
                this.percentLabel = new System.Windows.Forms.Label();
                this.percentRadioButton = new System.Windows.Forms.RadioButton();
                this.autoSizedRadioButton = new System.Windows.Forms.RadioButton();
                this.percentNumericUpDown = new System.Windows.Forms.NumericUpDown();
                this.addRemoveInsertTableLayoutPanel.SuspendLayout();
                this.okCancelTableLayoutPanel.SuspendLayout();
                this.overarchingTableLayoutPanel.SuspendLayout();
                this.showTableLayoutPanel.SuspendLayout();
                this.helperTextTableLayoutPanel.SuspendLayout();
                ((System.ComponentModel.ISupportInitialize)(this.infoPictureBox2)).BeginInit();
                ((System.ComponentModel.ISupportInitialize)(this.infoPictureBox1)).BeginInit();
                this.sizeTypeGroupBox.SuspendLayout();
                this.sizeTypeTableLayoutPanel.SuspendLayout();
                ((System.ComponentModel.ISupportInitialize)(this.absoluteNumericUpDown)).BeginInit();
                ((System.ComponentModel.ISupportInitialize)(this.percentNumericUpDown)).BeginInit();
                this.SuspendLayout();
                // 
                // addRemoveInsertTableLayoutPanel
                // 
                resources.ApplyResources(this.addRemoveInsertTableLayoutPanel, "addRemoveInsertTableLayoutPanel");
                this.addRemoveInsertTableLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33F));
                this.addRemoveInsertTableLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33F));
                this.addRemoveInsertTableLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33F));
                this.addRemoveInsertTableLayoutPanel.Controls.Add(this.addButton, 0, 0);
                this.addRemoveInsertTableLayoutPanel.Controls.Add(this.removeButton, 1, 0);
                this.addRemoveInsertTableLayoutPanel.Controls.Add(this.insertButton, 2, 0);
                this.addRemoveInsertTableLayoutPanel.Margin = new System.Windows.Forms.Padding(0, 3, 3, 3);
                this.addRemoveInsertTableLayoutPanel.Name = "addRemoveInsertTableLayoutPanel";
                this.addRemoveInsertTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
                // 
                // addButton
                // 
                resources.ApplyResources(this.addButton, "addButton");
                this.addButton.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
                this.addButton.Margin = new System.Windows.Forms.Padding(0, 0, 4, 0);
                this.addButton.MinimumSize = new System.Drawing.Size(75, 23);
                this.addButton.Name = "addButton";
                this.addButton.Padding = new System.Windows.Forms.Padding(10, 0, 10, 0);
                // 
                // removeButton
                // 
                resources.ApplyResources(this.removeButton, "removeButton");
                this.removeButton.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
                this.removeButton.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
                this.removeButton.MinimumSize = new System.Drawing.Size(75, 23);
                this.removeButton.Name = "removeButton";
                this.removeButton.Padding = new System.Windows.Forms.Padding(10, 0, 10, 0);
                // 
                // insertButton
                // 
                resources.ApplyResources(this.insertButton, "insertButton");
                this.insertButton.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
                this.insertButton.Margin = new System.Windows.Forms.Padding(4, 0, 0, 0);
                this.insertButton.MinimumSize = new System.Drawing.Size(75, 23);
                this.insertButton.Name = "insertButton";
                this.insertButton.Padding = new System.Windows.Forms.Padding(10, 0, 10, 0);
                // 
                // okCancelTableLayoutPanel
                // 
                resources.ApplyResources(this.okCancelTableLayoutPanel, "okCancelTableLayoutPanel");
                this.overarchingTableLayoutPanel.SetColumnSpan(this.okCancelTableLayoutPanel, 2);
                this.okCancelTableLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
                this.okCancelTableLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
                this.okCancelTableLayoutPanel.Controls.Add(this.okButton, 0, 0);
                this.okCancelTableLayoutPanel.Controls.Add(this.cancelButton, 1, 0);
                this.okCancelTableLayoutPanel.Margin = new System.Windows.Forms.Padding(0, 6, 0, 0);
                this.okCancelTableLayoutPanel.Name = "okCancelTableLayoutPanel";
                this.okCancelTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
                // 
                // okButton
                // 
                resources.ApplyResources(this.okButton, "okButton");
                this.okButton.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
                this.okButton.Margin = new System.Windows.Forms.Padding(0, 0, 3, 0);
                this.okButton.MinimumSize = new System.Drawing.Size(75, 23);
                this.okButton.Name = "okButton";
                this.okButton.Padding = new System.Windows.Forms.Padding(10, 0, 10, 0);
                // 
                // cancelButton
                // 
                resources.ApplyResources(this.cancelButton, "cancelButton");
                this.cancelButton.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
                this.cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
                this.cancelButton.Margin = new System.Windows.Forms.Padding(3, 0, 0, 0);
                this.cancelButton.MinimumSize = new System.Drawing.Size(75, 23);
                this.cancelButton.Name = "cancelButton";
                this.cancelButton.Padding = new System.Windows.Forms.Padding(10, 0, 10, 0);
                // 
                // overarchingTableLayoutPanel
                // 
                resources.ApplyResources(this.overarchingTableLayoutPanel, "overarchingTableLayoutPanel");
                this.overarchingTableLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
                this.overarchingTableLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
                this.overarchingTableLayoutPanel.Controls.Add(this.sizeTypeGroupBox, 1, 0);
                this.overarchingTableLayoutPanel.Controls.Add(this.okCancelTableLayoutPanel, 0, 4);
                this.overarchingTableLayoutPanel.Controls.Add(this.showTableLayoutPanel, 0, 0);
                this.overarchingTableLayoutPanel.Controls.Add(this.addRemoveInsertTableLayoutPanel, 0, 3);
                this.overarchingTableLayoutPanel.Controls.Add(this.columnsAndRowsListView, 0, 1);
                this.overarchingTableLayoutPanel.Controls.Add(this.helperTextTableLayoutPanel, 1, 2);
                this.overarchingTableLayoutPanel.Name = "overarchingTableLayoutPanel";
                this.overarchingTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
                this.overarchingTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
                this.overarchingTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
                this.overarchingTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
                this.overarchingTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
                // 
                // showTableLayoutPanel
                // 
                resources.ApplyResources(this.showTableLayoutPanel, "showTableLayoutPanel");
                this.showTableLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
                this.showTableLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
                this.showTableLayoutPanel.Controls.Add(this.memberTypeLabel, 0, 0);
                this.showTableLayoutPanel.Controls.Add(this.columnsOrRowsComboBox, 1, 0);
                this.showTableLayoutPanel.Margin = new System.Windows.Forms.Padding(0, 0, 3, 3);
                this.showTableLayoutPanel.Name = "showTableLayoutPanel";
                this.showTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
                // 
                // memberTypeLabel
                // 
                resources.ApplyResources(this.memberTypeLabel, "memberTypeLabel");
                this.memberTypeLabel.Margin = new System.Windows.Forms.Padding(0, 0, 3, 0);
                this.memberTypeLabel.Name = "memberTypeLabel";
                // 
                // columnsOrRowsComboBox
                // 
                resources.ApplyResources(this.columnsOrRowsComboBox, "columnsOrRowsComboBox");
                this.columnsOrRowsComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
                this.columnsOrRowsComboBox.FormattingEnabled = true;
                this.columnsOrRowsComboBox.Items.AddRange(new object[] {
            resources.GetString("columnsOrRowsComboBox.Items"),
            resources.GetString("columnsOrRowsComboBox.Items1")});
                this.columnsOrRowsComboBox.Margin = new System.Windows.Forms.Padding(3, 0, 0, 0);
                this.columnsOrRowsComboBox.Name = "columnsOrRowsComboBox";
                // 
                // columnsAndRowsListView
                // 
                resources.ApplyResources(this.columnsAndRowsListView, "columnsAndRowsListView");
                this.columnsAndRowsListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.membersColumnHeader,
            this.sizeTypeColumnHeader,
            this.valueColumnHeader});
                this.columnsAndRowsListView.FullRowSelect = true;
                this.columnsAndRowsListView.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.Nonclickable;
                this.columnsAndRowsListView.HideSelection = false;
                this.columnsAndRowsListView.Margin = new System.Windows.Forms.Padding(0, 3, 3, 3);
                this.columnsAndRowsListView.Name = "columnsAndRowsListView";
                this.overarchingTableLayoutPanel.SetRowSpan(this.columnsAndRowsListView, 2);
                this.columnsAndRowsListView.View = System.Windows.Forms.View.Details;
                // 
                // membersColumnHeader
                // 
                resources.ApplyResources(this.membersColumnHeader, "membersColumnHeader");
                // 
                // sizeTypeColumnHeader
                // 
                resources.ApplyResources(this.sizeTypeColumnHeader, "sizeTypeColumnHeader");
                // 
                // valueColumnHeader
                // 
                resources.ApplyResources(this.valueColumnHeader, "valueColumnHeader");
                // 
                // helperTextTableLayoutPanel
                // 
                resources.ApplyResources(this.helperTextTableLayoutPanel, "helperTextTableLayoutPanel");
                this.helperTextTableLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
                this.helperTextTableLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
                this.helperTextTableLayoutPanel.Controls.Add(this.infoPictureBox2, 0, 1);
                this.helperTextTableLayoutPanel.Controls.Add(this.infoPictureBox1, 0, 0);
                this.helperTextTableLayoutPanel.Controls.Add(this.helperLinkLabel1, 1, 0);
                this.helperTextTableLayoutPanel.Controls.Add(this.helperLinkLabel2, 1, 1);
                this.helperTextTableLayoutPanel.Margin = new System.Windows.Forms.Padding(6, 6, 0, 3);
                this.helperTextTableLayoutPanel.Name = "helperTextTableLayoutPanel";
                this.helperTextTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
                this.helperTextTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
                // 
                // infoPictureBox2
                // 
                resources.ApplyResources(this.infoPictureBox2, "infoPictureBox2");
                this.infoPictureBox2.Name = "infoPictureBox2";
                this.infoPictureBox2.TabStop = false;
                // 
                // infoPictureBox1
                // 
                resources.ApplyResources(this.infoPictureBox1, "infoPictureBox1");
                this.infoPictureBox1.Name = "infoPictureBox1";
                this.infoPictureBox1.TabStop = false;
                if (DpiHelper.IsScalingRequired)
                {
                    Bitmap bitmap = this.infoPictureBox1.Image as Bitmap;
                    DpiHelper.ScaleBitmapLogicalToDevice(ref bitmap);
                    this.infoPictureBox1.Image = bitmap;
                    bitmap = this.infoPictureBox2.Image as Bitmap;
                    DpiHelper.ScaleBitmapLogicalToDevice(ref bitmap);
                    this.infoPictureBox2.Image = bitmap;

                    this.scaledUpDownLeftMargin = DpiHelper.LogicalToDeviceUnitsX(UpDownLeftMargin);
                    this.scaledUpDownTopMargin = DpiHelper.LogicalToDeviceUnitsY(UpDownTopMargin);
                    this.scaledLabelRightMargin = DpiHelper.LogicalToDeviceUnitsX(LabelRightMargin);

                }
                // 
                // helperLinkLabel1
                // 
                resources.ApplyResources(this.helperLinkLabel1, "helperLinkLabel1");
                this.helperLinkLabel1.Margin = new System.Windows.Forms.Padding(3, 0, 0, 3);
                this.helperLinkLabel1.Name = "helperLinkLabel1";
                this.helperLinkLabel1.TabStop = true;
                this.helperLinkLabel1.UseCompatibleTextRendering = true;
                // 
                // helperLinkLabel2
                // 
                resources.ApplyResources(this.helperLinkLabel2, "helperLinkLabel2");
                this.helperLinkLabel2.Margin = new System.Windows.Forms.Padding(3, 3, 0, 0);
                this.helperLinkLabel2.Name = "helperLinkLabel2";
                this.helperLinkLabel2.TabStop = true;
                this.helperLinkLabel2.UseCompatibleTextRendering = true;
                // 
                // sizeTypeGroupBox
                // 
                resources.ApplyResources(this.sizeTypeGroupBox, "sizeTypeGroupBox");
                this.sizeTypeGroupBox.Controls.Add(this.sizeTypeTableLayoutPanel);
                this.sizeTypeGroupBox.Margin = new System.Windows.Forms.Padding(6, 0, 0, 3);
                this.sizeTypeGroupBox.Name = "sizeTypeGroupBox";
                this.sizeTypeGroupBox.Padding = new System.Windows.Forms.Padding(0);
                this.overarchingTableLayoutPanel.SetRowSpan(this.sizeTypeGroupBox, 2);
                this.sizeTypeGroupBox.TabStop = false;
                // 
                // sizeTypeTableLayoutPanel
                // 
                resources.ApplyResources(this.sizeTypeTableLayoutPanel, "sizeTypeTableLayoutPanel");
                this.sizeTypeTableLayoutPanel.RowStyles.Add(new Windows.Forms.RowStyle(SizeType.Percent, 33.3F));
                this.sizeTypeTableLayoutPanel.RowStyles.Add(new Windows.Forms.RowStyle(SizeType.Percent, 33.3F));
                this.sizeTypeTableLayoutPanel.RowStyles.Add(new Windows.Forms.RowStyle(SizeType.Percent, 33.3F));
                this.sizeTypeTableLayoutPanel.Controls.Add(this.absoluteNumericUpDown, 1, 0);
                this.sizeTypeTableLayoutPanel.Controls.Add(this.absoluteRadioButton, 0, 0);
                this.sizeTypeTableLayoutPanel.Controls.Add(this.pixelsLabel, 2, 0);
                this.sizeTypeTableLayoutPanel.Controls.Add(this.percentLabel, 2, 1);
                this.sizeTypeTableLayoutPanel.Controls.Add(this.percentRadioButton, 0, 1);
                this.sizeTypeTableLayoutPanel.Controls.Add(this.autoSizedRadioButton, 0, 2);
                this.sizeTypeTableLayoutPanel.Controls.Add(this.percentNumericUpDown, 1, 1);
                this.sizeTypeTableLayoutPanel.Margin = new System.Windows.Forms.Padding(9);
                this.sizeTypeTableLayoutPanel.Name = "sizeTypeTableLayoutPanel";
                this.sizeTypeTableLayoutPanel.AutoSize = true;
                this.sizeTypeTableLayoutPanel.AutoSizeMode = Forms.AutoSizeMode.GrowAndShrink;
                // 
                // absoluteNumericUpDown
                // 
                resources.ApplyResources(this.absoluteNumericUpDown, "absoluteNumericUpDown");
                this.absoluteNumericUpDown.Maximum = new decimal(new int[] {
            99999,
            0,
            0,
            0});
                this.absoluteNumericUpDown.Name = "absoluteNumericUpDown";
                this.absoluteNumericUpDown.Margin = new System.Windows.Forms.Padding(scaledUpDownLeftMargin, scaledUpDownTopMargin, 0, 0);
                this.absoluteNumericUpDown.AutoScaleMode = Forms.AutoScaleMode.Font;
                // 
                // absoluteRadioButton
                // 
                resources.ApplyResources(this.absoluteRadioButton, "absoluteRadioButton");
                this.absoluteRadioButton.Margin = new System.Windows.Forms.Padding(0, 3, 3, 0);
                this.absoluteRadioButton.Name = "absoluteRadioButton";
                // 
                // pixelsLabel
                // 
                resources.ApplyResources(this.pixelsLabel, "pixelsLabel");
                this.pixelsLabel.Name = "pixelsLabel";
                this.pixelsLabel.Margin = new Forms.Padding(0, 0, scaledLabelRightMargin, 0);
                // 
                // percentLabel
                // 
                resources.ApplyResources(this.percentLabel, "percentLabel");
                this.percentLabel.Name = "percentLabel";
                this.percentLabel.Margin = new Forms.Padding(0, 0, scaledLabelRightMargin, 0);
                // 
                // percentRadioButton
                // 
                resources.ApplyResources(this.percentRadioButton, "percentRadioButton");
                this.percentRadioButton.Margin = new System.Windows.Forms.Padding(0, 3, 3, 0);
                this.percentRadioButton.Name = "percentRadioButton";
                // 
                // autoSizedRadioButton
                // 
                resources.ApplyResources(this.autoSizedRadioButton, "autoSizedRadioButton");
                this.autoSizedRadioButton.Margin = new System.Windows.Forms.Padding(0, 3, 3, 0);
                this.autoSizedRadioButton.Name = "autoSizedRadioButton";
                // 
                // percentNumericUpDown
                // 
                resources.ApplyResources(this.percentNumericUpDown, "percentNumericUpDown");
                this.percentNumericUpDown.DecimalPlaces = 2;
                this.percentNumericUpDown.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
                this.percentNumericUpDown.Name = "percentNumericUpDown";
                this.percentNumericUpDown.Margin = new System.Windows.Forms.Padding(scaledUpDownLeftMargin, scaledUpDownTopMargin, 0, 0);
                this.percentNumericUpDown.AutoScaleMode = Forms.AutoScaleMode.Font;
                // 
                // StyleCollectionEditor
                // 
                this.AcceptButton = this.okButton;
                resources.ApplyResources(this, "$this");
                this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
                this.CancelButton = this.cancelButton;
                this.Controls.Add(this.overarchingTableLayoutPanel);
                this.HelpButton = true;
                this.MaximizeBox = false;
                this.MinimizeBox = false;
                this.Name = "Form1";
                this.ShowIcon = false;
                this.ShowInTaskbar = false;
                this.addRemoveInsertTableLayoutPanel.ResumeLayout(false);
                this.addRemoveInsertTableLayoutPanel.PerformLayout();
                this.okCancelTableLayoutPanel.ResumeLayout(false);
                this.okCancelTableLayoutPanel.PerformLayout();
                this.overarchingTableLayoutPanel.ResumeLayout(false);
                this.overarchingTableLayoutPanel.PerformLayout();
                this.showTableLayoutPanel.ResumeLayout(false);
                this.showTableLayoutPanel.PerformLayout();
                this.helperTextTableLayoutPanel.ResumeLayout(false);
                this.helperTextTableLayoutPanel.PerformLayout();
                ((System.ComponentModel.ISupportInitialize)(this.infoPictureBox2)).EndInit();
                ((System.ComponentModel.ISupportInitialize)(this.infoPictureBox1)).EndInit();
                this.sizeTypeGroupBox.ResumeLayout(false);
                this.sizeTypeTableLayoutPanel.ResumeLayout(false);
                this.sizeTypeTableLayoutPanel.PerformLayout();
                ((System.ComponentModel.ISupportInitialize)(this.absoluteNumericUpDown)).EndInit();
                ((System.ComponentModel.ISupportInitialize)(this.percentNumericUpDown)).EndInit();
                this.ResumeLayout(false);

            }

            #endregion            

            private void OnShown(object sender, EventArgs e)
            {
                //We need to set the dirty flag here, since the initialization down above, might
                //cause it to get set in one of the methods below.
                isDialogDirty = false;
                this.columnsAndRowsListView.AutoResizeColumns(ColumnHeaderAutoResizeStyle.HeaderSize);
            }

            private void OnLink1Click(object sender, LinkLabelLinkClickedEventArgs e)
            {
                CancelEventArgs c = new CancelEventArgs();
                editor.helptopic = "net.ComponentModel.StyleCollectionEditor.TLP.SpanRowsColumns";
                OnHelpButtonClicked(sender, c);
            }

            private void OnLink2Click(object sender, LinkLabelLinkClickedEventArgs e)
            {
                CancelEventArgs c = new CancelEventArgs();
                editor.helptopic = "net.ComponentModel.StyleCollectionEditor.TLP.AnchorDock";
                OnHelpButtonClicked(sender, c);
            }

            private void OnHelpButtonClicked(object sender, CancelEventArgs e)
            {
                e.Cancel = true;
                editor.helptopic = "net.ComponentModel.StyleCollectionEditor";
                editor.ShowHelp();
            }

            protected override void OnEditValueChanged()
            {
            }

            protected internal override DialogResult ShowEditorDialog(IWindowsFormsEditorService edSvc)
            {

                if (compSvc != null)
                {
                    if (rowStyleProp != null)
                    {
                        compSvc.OnComponentChanging(tlp, rowStyleProp);
                    }

                    if (colStyleProp != null)
                    {
                        compSvc.OnComponentChanging(tlp, colStyleProp);
                    }
                }

                // We can't use ColumnCount/RowCount, since they are only guaranteed to reflect the true count
                // when GrowMode == Fixed
                int[] cw = tlp.GetColumnWidths();
                int[] rh = tlp.GetRowHeights();

                // We should have at least as many <Col|Row>Styles as <Cols|Rows> -- the designer guarantees that
                Debug.Assert(tlp.ColumnStyles.Count >= cw.Length, "Why is ColumnStyle.Count not the same as ColumnCount?");
                Debug.Assert(tlp.RowStyles.Count >= rh.Length, "Why is RowStyle.Count not the same as RowCount?");

                // If we have more, then let's remove the extra ones. This is because we don't want any new row/col that we might add
                // to inherit leftover styles. We want to make sure than any new rol/col is of type Absolute and of size 20.
                if (tlp.ColumnStyles.Count > cw.Length)
                {
                    int diff = tlp.ColumnStyles.Count - cw.Length;
                    for (int i = 0; i < diff; ++i)
                    {
                        tlp.ColumnStyles.RemoveAt(tlp.ColumnStyles.Count - 1);
                    }
                }

                if (tlp.RowStyles.Count > rh.Length)
                {
                    int diff = tlp.RowStyles.Count - rh.Length;
                    for (int i = 0; i < diff; ++i)
                    {
                        tlp.RowStyles.RemoveAt(tlp.RowStyles.Count - 1);
                    }
                }

                //this will cause the listview to be initialized
                columnsOrRowsComboBox.SelectedIndex = isRowCollection ? 1 : 0;
                InitListView();

                return base.ShowEditorDialog(edSvc);
            }

            private string FormatValueString(SizeType type, float value)
            {
                if (type == SizeType.Absolute)
                {
                    return value.ToString(CultureInfo.CurrentCulture);
                }
                else if (type == SizeType.Percent)
                {
                    // value will be multiplied by 100, so let's adjust for that
                    return (value / 100).ToString("P", CultureInfo.CurrentCulture);
                }
                else
                {
                    return string.Empty;
                }
            }

            // Populate the listview with the correct values - happens when the dialog is brought up, or
            // when the user changes the selection in the combobox
            private void InitListView()
            {
                columnsAndRowsListView.Items.Clear();

                string baseName = isRowCollection ? "Row" : "Column"; //these should not be localized

                int styleCount = isRowCollection ? tlp.RowStyles.Count : tlp.ColumnStyles.Count;

                for (int i = 0; i < styleCount; ++i)
                {
                    string sizeType;
                    string sizeValue;

                    if (isRowCollection)
                    {
                        RowStyle rowStyle = tlp.RowStyles[i];
                        sizeType = rowStyle.SizeType.ToString();
                        sizeValue = FormatValueString(rowStyle.SizeType, rowStyle.Height);
                    }
                    else
                    {
                        ColumnStyle colStyle = tlp.ColumnStyles[i];
                        sizeType = colStyle.SizeType.ToString();
                        sizeValue = FormatValueString(colStyle.SizeType, colStyle.Width);
                    }

                    //We add 1, since we want the Member to read <Column|Row>1,2,3... 
                    columnsAndRowsListView.Items.Add(new ListViewItem(new string[] { baseName + (i + 1).ToString(CultureInfo.InvariantCulture), sizeType, sizeValue }));
                }

                if (styleCount > 0)
                {
                    ClearAndSetSelectionAndFocus(0);
                }

                removeButton.Enabled = columnsAndRowsListView.Items.Count > 1; // we should already have something selected
            }

            private void UpdateListViewItem(int index, string member, string type, string value)
            {
                columnsAndRowsListView.Items[index].SubItems[MEMBER_INDEX].Text = member;
                columnsAndRowsListView.Items[index].SubItems[TYPE_INDEX].Text = type;
                columnsAndRowsListView.Items[index].SubItems[VALUE_INDEX].Text = value;
            }

            private void UpdateListViewMember()
            {
                // let's do a for loop rather than for-each, don't have to do the object creation
                for (int i = 0; i < columnsAndRowsListView.Items.Count; ++i)
                {
                    columnsAndRowsListView.Items[i].SubItems[MEMBER_INDEX].Text = (isRowCollection ? "Row" : "Column") + (i + 1).ToString(CultureInfo.InvariantCulture);
                }
            }

            private void OnComboBoxSelectionChangeCommitted(object sender, EventArgs e)
            {
                isRowCollection = columnsOrRowsComboBox.SelectedIndex != 0;
                InitListView();
            }

            private void OnListViewSelectedIndexChanged(object sender, EventArgs e)
            {
                ListView.SelectedListViewItemCollection coll = columnsAndRowsListView.SelectedItems;

                if (coll.Count == 0)
                {
                    //When the selection changes from one item to another, we will get a temporary state
                    //where the selection collection is empty. We don't want to disable buttons in this case
                    //since that will cause flashing, so let's do a begininvoke here. In the delegate we check
                    //if the collection really is empty and then disable buttons and whatever else we have to do.
                    if (!haveInvoked)
                    {
                        BeginInvoke(new EventHandler(this.OnListSelectionComplete));
                        haveInvoked = true;
                    }
                    return;
                }

                sizeTypeGroupBox.Enabled = true;
                insertButton.Enabled = true;
                if (coll.Count == columnsAndRowsListView.Items.Count)
                {
                    removeButton.Enabled = false;
                }
                else
                {
                    removeButton.Enabled = columnsAndRowsListView.Items.Count > 1;
                }

                if (coll.Count == 1)
                {
                    //Get the index
                    int index = columnsAndRowsListView.Items.IndexOf(coll[0]);
                    if (isRowCollection)
                    {
                        UpdateGroupBox(tlp.RowStyles[index].SizeType, tlp.RowStyles[index].Height);
                    }
                    else
                    {
                        UpdateGroupBox(tlp.ColumnStyles[index].SizeType, tlp.ColumnStyles[index].Width);
                    }
                }
                else
                {
                    //multi-selection

                    //Check if all the items in the selection are of the same type and value

                    SizeType type;
                    float value = 0;

                    bool sameValues = true;

                    int index = columnsAndRowsListView.Items.IndexOf(coll[0]);

                    type = isRowCollection ? tlp.RowStyles[index].SizeType : tlp.ColumnStyles[index].SizeType;
                    value = isRowCollection ? tlp.RowStyles[index].Height : tlp.ColumnStyles[index].Width;
                    for (int i = 1; i < coll.Count; i++)
                    {
                        index = columnsAndRowsListView.Items.IndexOf(coll[i]);
                        if (type != (isRowCollection ? tlp.RowStyles[index].SizeType : tlp.ColumnStyles[index].SizeType))
                        {
                            sameValues = false;
                            break;
                        }

                        if (value != (isRowCollection ? tlp.RowStyles[index].Height : tlp.ColumnStyles[index].Width))
                        {
                            sameValues = false;
                            break;
                        }
                    }

                    if (!sameValues)
                    {
                        ResetAllRadioButtons();
                    }
                    else
                    {
                        UpdateGroupBox(type, value);
                    }
                }
            }

            private void OnListSelectionComplete(object sender, EventArgs e)
            {
                haveInvoked = false;
                // Nothing is truly selected in the listview
                if (columnsAndRowsListView.SelectedItems.Count == 0)
                {
                    ResetAllRadioButtons();
                    sizeTypeGroupBox.Enabled = false;
                    insertButton.Enabled = false;
                    removeButton.Enabled = false;
                }
            }

            private void ResetAllRadioButtons()
            {
                absoluteRadioButton.Checked = false;
                ResetAbsolute();

                percentRadioButton.Checked = false;
                ResetPercent();

                autoSizedRadioButton.Checked = false;
            }

            private void ResetAbsolute()
            {
                //Unhook the event while we reset.
                //If we didn't the setting the value would cause OnValueChanged below to get called.
                //If we then go ahead and update the listview, which we don't want in the reset case.
                this.absoluteNumericUpDown.ValueChanged -= new System.EventHandler(this.OnValueChanged);
                absoluteNumericUpDown.Enabled = false;
                absoluteNumericUpDown.Value = DesignerUtils.MINIMUMSTYLESIZE;
                this.absoluteNumericUpDown.ValueChanged += new System.EventHandler(this.OnValueChanged);
            }

            private void ResetPercent()
            {
                //Unhook the event while we reset.
                //If we didn't the setting the value would cause OnValueChanged below to get called.
                //If we then go ahead and update the listview, which we don't want in the reset case.
                this.percentNumericUpDown.ValueChanged -= new System.EventHandler(this.OnValueChanged);
                percentNumericUpDown.Enabled = false;
                percentNumericUpDown.Value = DesignerUtils.MINIMUMSTYLEPERCENT;
                this.percentNumericUpDown.ValueChanged += new System.EventHandler(this.OnValueChanged);
            }

            private void UpdateGroupBox(SizeType type, float value)
            {
                switch (type)
                {
                    case SizeType.Absolute:
                        absoluteRadioButton.Checked = true;
                        absoluteNumericUpDown.Enabled = true;
                        try
                        {
                            absoluteNumericUpDown.Value = (Decimal)value;
                        }

                        catch (System.ArgumentOutOfRangeException)
                        {
                            absoluteNumericUpDown.Value = DesignerUtils.MINIMUMSTYLESIZE;
                        }
                        ResetPercent();
                        break;
                    case SizeType.Percent:
                        percentRadioButton.Checked = true;
                        percentNumericUpDown.Enabled = true;
                        try
                        {
                            percentNumericUpDown.Value = (Decimal)value;
                        }

                        catch (System.ArgumentOutOfRangeException)
                        {
                            percentNumericUpDown.Value = DesignerUtils.MINIMUMSTYLEPERCENT;
                        }

                        ResetAbsolute();
                        break;
                    case SizeType.AutoSize:
                        autoSizedRadioButton.Checked = true;
                        ResetAbsolute();
                        ResetPercent();
                        break;
                    default:
                        Debug.Fail("Unsupported SizeType");
                        break;
                }
            }

            private void ClearAndSetSelectionAndFocus(int index)
            {
                columnsAndRowsListView.BeginUpdate();
                columnsAndRowsListView.Focus();
                if (columnsAndRowsListView.FocusedItem != null)
                {
                    columnsAndRowsListView.FocusedItem.Focused = false;
                }
                columnsAndRowsListView.SelectedItems.Clear();
                columnsAndRowsListView.Items[index].Selected = true;
                columnsAndRowsListView.Items[index].Focused = true;
                columnsAndRowsListView.Items[index].EnsureVisible();
                columnsAndRowsListView.EndUpdate();
            }

            /// <devdoc>
            ///     Adds an item to the listview at the specified index
            /// </devdoc>
            private void AddItem(int index)
            {
                string member = null;

                tlpDesigner.InsertRowCol(isRowCollection, index);

                if (isRowCollection)
                {
                    member = "Row" + tlp.RowStyles.Count.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    member = "Column" + tlp.RowStyles.Count.ToString(CultureInfo.InvariantCulture);
                }


                if (member != null)
                {
                    columnsAndRowsListView.Items.Insert(index, new ListViewItem(new string[] { member, SizeType.Absolute.ToString(), DesignerUtils.MINIMUMSTYLESIZE.ToString(CultureInfo.InvariantCulture) }));

                    // If we inserted at the beginning, then we have to change the Member of string of all the existing listview items,
                    // so we might as well just update the entire listview.
                    UpdateListViewMember();
                    ClearAndSetSelectionAndFocus(index);
                }

            }

            private void OnAddButtonClick(object sender, EventArgs e)
            {
                isDialogDirty = true;
                // Add an item to the end of the listview
                AddItem(columnsAndRowsListView.Items.Count);
            }

            private void OnInsertButtonClick(object sender, EventArgs e)
            {
                isDialogDirty = true;
                // Insert an item before the 1st selected item
                AddItem(columnsAndRowsListView.SelectedIndices[0]);
                tlpDesigner.FixUpControlsOnInsert(isRowCollection, columnsAndRowsListView.SelectedIndices[0]);
            }

            private void OnRemoveButtonClick(object sender, EventArgs e)
            {

                if ((columnsAndRowsListView.Items.Count == 1) || (columnsAndRowsListView.Items.Count == columnsAndRowsListView.SelectedIndices.Count))
                {
                    Debug.Fail("We should never get here!");
                    return; // we can't remove anything when we have just 1 row/col or if all rows/cols are selected
                }

                isDialogDirty = true;

                // store the new index 
                int newIndex = columnsAndRowsListView.SelectedIndices[0];

                // Remove from the end of the selection -- that way we have less things to adjust
                for (int i = columnsAndRowsListView.SelectedIndices.Count - 1; i >= 0; i--)
                {
                    int index = columnsAndRowsListView.SelectedIndices[i];

                    // First update any controls in any row/col that's AFTER the row/col we are deleting
                    tlpDesigner.FixUpControlsOnDelete(isRowCollection, index, deleteList);
                    // Then remove the row/col
                    tlpDesigner.DeleteRowCol(isRowCollection, index);

                    // Then remove the listview item
                    if (isRowCollection)
                    {
                        columnsAndRowsListView.Items.RemoveAt(index);
                    }
                    else
                    {
                        columnsAndRowsListView.Items.RemoveAt(index);
                    }
                }

                if (newIndex >= columnsAndRowsListView.Items.Count)
                {
                    newIndex -= 1;
                }

                // If we removed at the beginning, then we have to change the Member of string of all the existing listview items,
                // so we might as well just update the entire listview.
                UpdateListViewMember();
                ClearAndSetSelectionAndFocus(newIndex);
            }

            private void UpdateTypeAndValue(SizeType type, float value)
            {

                for (int i = 0; i < columnsAndRowsListView.SelectedIndices.Count; i++)
                {
                    int index = columnsAndRowsListView.SelectedIndices[i];

                    if (isRowCollection)
                    {
                        tlp.RowStyles[index].SizeType = type;
                        tlp.RowStyles[index].Height = value;
                    }
                    else
                    {

                        tlp.ColumnStyles[index].SizeType = type;
                        tlp.ColumnStyles[index].Width = value;
                    }

                    UpdateListViewItem(index, columnsAndRowsListView.Items[index].SubItems[MEMBER_INDEX].Text, type.ToString(), FormatValueString(type, value));
                }
            }

            private void OnAbsoluteEnter(object sender, EventArgs e)
            {
                isDialogDirty = true;
                UpdateTypeAndValue(SizeType.Absolute, (float)absoluteNumericUpDown.Value);
                absoluteNumericUpDown.Enabled = true;
                ResetPercent();
            }

            private void OnPercentEnter(object sender, EventArgs e)
            {
                isDialogDirty = true;
                UpdateTypeAndValue(SizeType.Percent, (float)percentNumericUpDown.Value);
                percentNumericUpDown.Enabled = true;
                ResetAbsolute();
            }

            private void OnAutoSizeEnter(object sender, EventArgs e)
            {
                isDialogDirty = true;
                UpdateTypeAndValue(SizeType.AutoSize, 0);
                ResetAbsolute();
                ResetPercent();
            }

            private void OnValueChanged(object sender, EventArgs e)
            {
                if (absoluteNumericUpDown == sender && absoluteRadioButton.Checked)
                {
                    isDialogDirty = true;
                    UpdateTypeAndValue(SizeType.Absolute, (float)absoluteNumericUpDown.Value);
                }
                else if (percentNumericUpDown == sender && percentRadioButton.Checked)
                {
                    isDialogDirty = true;
                    UpdateTypeAndValue(SizeType.Percent, (float)percentNumericUpDown.Value);
                }
            }

            private void NormalizePercentStyle(bool normalizeRow)
            {
                int count = normalizeRow ? tlp.RowStyles.Count : tlp.ColumnStyles.Count;
                float total = 0;
                // Loop through an calculate the percentage total
                for (int i = 0; i < count; i++)
                {
                    if (normalizeRow)
                    {
                        if (tlp.RowStyles[i].SizeType != SizeType.Percent)
                        {
                            continue;
                        }

                        total += tlp.RowStyles[i].Height;
                    }
                    else
                    {
                        if (tlp.ColumnStyles[i].SizeType != SizeType.Percent)
                        {
                            continue;
                        }

                        total += tlp.ColumnStyles[i].Width;
                    }
                }

                // we're outta here...
                if ((total == 100) || (total == 0))
                {
                    return;
                }

                // now loop through and set the normalized value
                for (int i = 0; i < count; i++)
                {
                    if (normalizeRow)
                    {
                        if (tlp.RowStyles[i].SizeType != SizeType.Percent)
                        {
                            continue;
                        }

                        tlp.RowStyles[i].Height = (tlp.RowStyles[i].Height * 100) / total;
                    }
                    else
                    {
                        if (tlp.ColumnStyles[i].SizeType != SizeType.Percent)
                        {
                            continue;
                        }

                        tlp.ColumnStyles[i].Width = (tlp.ColumnStyles[i].Width * 100) / total;
                    }
                }
            }

            /// <devdoc>
            /// Take all the styles of SizeType.Percent and normalize them to 100%
            /// </devdoc>
            private void NormalizePercentStyles()
            {
                NormalizePercentStyle(true /* row */);
                NormalizePercentStyle(false /* column */);
            }

            private void OnOkButtonClick(object sender, EventArgs e)
            {
                if (isDialogDirty)
                {
                    if (absoluteRadioButton.Checked)
                    {
                        UpdateTypeAndValue(SizeType.Absolute, (float)absoluteNumericUpDown.Value);
                    }
                    else if (percentRadioButton.Checked)
                    {
                        UpdateTypeAndValue(SizeType.Percent, (float)percentNumericUpDown.Value);
                    }
                    else if (autoSizedRadioButton.Checked)
                    {
                        UpdateTypeAndValue(SizeType.AutoSize, 0);
                    }

                    // Now normalize all percentages...
                    NormalizePercentStyles();

                    // IF YOU CHANGE THIS, YOU SHOULD ALSO CHANGE THE CODE IN TableLayoutPanelDesigner.OnRemoveInternal
                    if (deleteList.Count > 0)
                    {

                        PropertyDescriptor childProp = TypeDescriptor.GetProperties(tlp)["Controls"];
                        if (compSvc != null && childProp != null)
                        {
                            compSvc.OnComponentChanging(tlp, childProp);
                        }

                        IDesignerHost host = tlp.Site.GetService(typeof(IDesignerHost)) as IDesignerHost;

                        if (host != null)
                        {
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
                        }

                        if (compSvc != null && childProp != null)
                        {
                            compSvc.OnComponentChanged(tlp, childProp, null, null);
                        }
                    }

                    if (compSvc != null)
                    {
                        if (rowStyleProp != null)
                        {
                            compSvc.OnComponentChanged(tlp, rowStyleProp, null, null);
                        }

                        if (colStyleProp != null)
                        {
                            compSvc.OnComponentChanged(tlp, colStyleProp, null, null);
                        }
                    }

                    DialogResult = DialogResult.OK;
                }
                else
                {
                    DialogResult = DialogResult.Cancel;
                }
                tlpDesigner.ResumeEnsureAvailableStyles(true);
                tlp.ResumeLayout();
            }

            private void OnCancelButtonClick(object sender, EventArgs e)
            {
                tlpDesigner.ResumeEnsureAvailableStyles(false);
                tlp.ResumeLayout();
                DialogResult = DialogResult.Cancel;
            }
        }
    }
}
