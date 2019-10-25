using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms.Design.Behavior;

namespace System.Windows.Forms.Design
{
    /// <include file='doc\FlowPanelDesigner.uex' path='docs/doc[@for="FlowPanelDesigner"]/*' />
    /// <devdoc>
    ///     Base class to our flow control designers: TableLayoutPanel and FlowLayoutPanel.
    ///     This class shares common operations for these designers including:
    ///     Stripping all padding SnapLines, adding children, and refreshing selection 
    ///     after a drag-drop operation - since these types of controls will always 
    ///     reposition children.
    /// </devdoc>
    internal class FlowPanelDesigner : PanelDesigner
    {

        /// <include file='doc\FlowPanelDesigner.uex' path='docs/doc[@for="FlowPanelDesigner.ParticipatesWidthSnapLines"]/*' />
        /// <devdoc>
        ///     Overridden to disallow SnapLines during drag operations if the primary drag control
        ///     is over the FlowPanelDesigner.
        /// </devdoc>
        public override bool ParticipatesWithSnapLines
        {
            get
            {
                return false;
            }
        }

        /// <include file='doc\FlowPanelDesigner.uex' path='docs/doc[@for="FlowPanelDesigner.SnapLines"]/*' />
        /// <devdoc>
        ///     Get the standard SnapLines from our ParentControlDesigner then
        ///     strips all the padding lines - since we don't want these guys
        ///     for flow designers.
        public override IList SnapLines
        {
            get
            {
                ArrayList snapLines = (ArrayList)base.SnapLines;

                //identify all the paddings to remove 
                ArrayList paddingsToRemove = new ArrayList(4);
                foreach (SnapLine line in snapLines)
                {
                    if (line.Filter != null && line.Filter.Contains(SnapLine.Padding))
                    {
                        paddingsToRemove.Add(line);
                    }
                }

                //remove all padding
                foreach (SnapLine line in paddingsToRemove)
                {
                    snapLines.Remove(line);
                }
                return snapLines;

            }
        }

        /// <include file='doc\FlowPanelDesigner.uex' path='docs/doc[@for="FlowPanelDesigner.AddChildControl"]/*' />
        /// <devdoc>
        ///     Overrides the base and skips the adjustment of the child position
        ///     since the runtime control will re-position this for us.
        /// </devdoc>
        internal override void AddChildControl(Control newChild)
        {
            // Skip location adjustment because FlowPanel is going to position this control.

            // Also, Skip z-order adjustment because SendToFront will put the new control at the
            // beginning of the flow instead of the end, plus FlowLayout is already preventing
            // overlap.
            this.Control.Controls.Add(newChild);
        }

        /// <include file='doc\FlowPanelDesigner.uex' path='docs/doc[@for="FlowPanelDesigner.OnDragDrop"]/*' />
        /// <devdoc>
        ///     We override this, call base, then attempt to re-sync the selection
        ///     since the control will most likely re-position a child for us.
        /// </devdoc>
        protected override void OnDragDrop(DragEventArgs de)
        {
            base.OnDragDrop(de);

            SelectionManager sm = GetService(typeof(SelectionManager)) as SelectionManager;
            if (sm != null)
            {
                sm.Refresh();
            }
        }
    }
}
