using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;

namespace System.Windows.Forms.Automation
{
    internal class UiaTextRange : UnsafeNativeMethods.ITextRangeProvider
    {
        public UiaTextRange(UiaTextProvider provider, int start, int end)
        {
            if (start < 0 || end < start)
            {
                throw new InvalidOperationException("Character offset not valid in TextRange.");
            }

            Debug.Assert(provider != null);

            _provider = provider;
            _start = start;
            _end = end;
        }

        #region Public Methods

        UnsafeNativeMethods.ITextRangeProvider UnsafeNativeMethods.ITextRangeProvider.Clone()
        {
            return new UiaTextRange(_provider, Start, End);
        }

        bool UnsafeNativeMethods.ITextRangeProvider.Compare(UnsafeNativeMethods.ITextRangeProvider range)
        {
            // Ranges come from the same element. Only need to compare endpoints.
            UiaTextRange editRange = (UiaTextRange)range;
            return editRange.Start == Start
                && editRange.End == End;
        }

        int UnsafeNativeMethods.ITextRangeProvider.CompareEndpoints(
            UnsafeNativeMethods.TextPatternRangeEndpoint endpoint,
            UnsafeNativeMethods.ITextRangeProvider targetRange,
            UnsafeNativeMethods.TextPatternRangeEndpoint targetEndpoint)
        {
            UiaTextRange editRange = (UiaTextRange)targetRange;
            int e1 = (endpoint == (int)UnsafeNativeMethods.TextPatternRangeEndpoint.Start)
                ? Start : End;
            int e2 = (targetEndpoint == (int)UnsafeNativeMethods.TextPatternRangeEndpoint.Start)
                ? editRange.Start : editRange.End;
            return e1 - e2;
        }

        void UnsafeNativeMethods.ITextRangeProvider.ExpandToEnclosingUnit(UnsafeNativeMethods.TextUnit unit)
        {
            switch (unit)
            {
                case UnsafeNativeMethods.TextUnit.Character:
                    // Leave it as it is except the case with 0-range.
                    if (Start == End)
                    {
                        End = MoveEndpointForward(End, UnsafeNativeMethods.TextUnit.Character, 1, out int moved);
                    }

                    break;

                case UnsafeNativeMethods.TextUnit.Word:
                    {
                        // Get the word boundaries.
                        string text = _provider.GetText();
                        ValidateEndpoints();

                        // Move start left until we reach a word boundary.
                        while (!AtWordBoundary(text, Start))
                        {
                            Start--;
                        }

                        // Move end right until we reach word boundary (different from Start).
                        End = Math.Min(Math.Max(End, Start + 1), text.Length);
                        while (!AtWordBoundary(text, End))
                        {
                            End++;
                        }
                    }

                    break;

                case UnsafeNativeMethods.TextUnit.Line:
                    {
                        if (_provider.GetLineCount() != 1)
                        {
                            int startLine = _provider.GetLineFromChar(Start);
                            int endLine = _provider.GetLineFromChar(End);

                            MoveTo(_provider.GetLineIndex(startLine), _provider.GetLineIndex(endLine + 1));
                        }
                        else
                        {
                            MoveTo(0, _provider.GetTextLength());
                        }
                    }
                    break;

                case UnsafeNativeMethods.TextUnit.Paragraph:
                    {
                        // Get the paragraph boundaries.
                        string text = _provider.GetText();
                        ValidateEndpoints();

                        // Move start left until we reach a paragraph boundary.
                        while(!AtParagraphBoundary(text, Start))
                        {
                            Start--;
                        }

                        // Move end right until we reach a paragraph boundary (different from Start).
                        End = Math.Min(Math.Max(End, Start + 1), text.Length);
                        while(!AtParagraphBoundary(text, End))
                        {
                            End++;
                        }
                    }
                    break;

                case UnsafeNativeMethods.TextUnit.Format:
                case UnsafeNativeMethods.TextUnit.Page:
                case UnsafeNativeMethods.TextUnit.Document:
                    MoveTo(0, _provider.GetTextLength());
                    break;

                default:
                    throw new InvalidEnumArgumentException("unit", (int)unit, typeof(UnsafeNativeMethods.TextUnit));
            }
        }

        UnsafeNativeMethods.ITextRangeProvider UnsafeNativeMethods.ITextRangeProvider.FindAttribute(int attributeId, object val, bool backwards)
        {
            throw new NotImplementedException("ITextRangeProvider.FindAttribute is not implemented.");
        }

        UnsafeNativeMethods.ITextRangeProvider UnsafeNativeMethods.ITextRangeProvider.FindText(string text, bool backwards, bool ignoreCase)
        {
            if (text == null)
            {
                throw new ArgumentNullException("TextRange: argument 'text' should not be null.");
            }
            if (text.Length == 0)
            {
                throw new ArgumentException("TextRange: argument 'text' is not valid: length should be more than 0.");
            }

            string rangeText = _provider.GetText();
            ValidateEndpoints();
            rangeText = rangeText.Substring(Start, Length);

            if (ignoreCase)
            {
                rangeText = rangeText.ToLower(CultureInfo.InvariantCulture);
                text = text.ToLower(CultureInfo.InvariantCulture);
            }

            // Do a case-sensitive search for the text inside the range.
            int i = backwards ? rangeText.LastIndexOf(text, StringComparison.Ordinal) : rangeText.IndexOf(text, StringComparison.Ordinal);

            // If the text was found then create a new range covering the found text.
            return i >= 0 ? new UiaTextRange(_provider, Start + i, Start + i + text.Length) : null;
        }

        object UnsafeNativeMethods.ITextRangeProvider.GetAttributeValue(int attributeId)
        {
            throw new NotImplementedException("ITextRangeProvider.GetAttributeValue is not implemented.");
        }

        double[] UnsafeNativeMethods.ITextRangeProvider.GetBoundingRectangles()
        {
            // Return zero rectangles for a degenerate-range. We don't return an empty, 
            // but properly positioned, rectangle for degenerate ranges.
            if (IsDegenerate)
            {
                return new double[0];
            }

            string text = _provider.GetText();
            ValidateEndpoints();

            // get the mapping from client coordinates to screen coordinates
            NativeMethods.Win32Point w32point;
            w32point.x = 0;
            w32point.y = 0;

            // TODO: Implement mapping points.
            // if (!Misc.MapWindowPoints(_provider.WindowHandle, IntPtr.Zero, ref w32point, 1))
            // {
            //     return new double[0];
            // }

            Point mapClientToScreen = new Point(w32point.x, w32point.y);

            // Clip the rectangles to the edit control's formatting rectangle
            Rectangle clippingRectangle = _provider.GetRectangle();

            // We accumulate rectangles onto a list.
            Collections.ArrayList rectangles;

            if (_provider.IsMultiline)
            {
                rectangles = GetMultilineBoundingRectangles(text, mapClientToScreen, clippingRectangle);
            }
            else
            {
                rectangles = new Collections.ArrayList(1);

                // figure out the rectangle for this one line
                Point startPoint = _provider.GetPositionFromChar(Start);
                Point endPoint = _provider.GetPositionFromCharUR(End - 1, text);
                Rectangle rectangle = new Rectangle(startPoint.X, startPoint.Y, endPoint.X - startPoint.X, clippingRectangle.Height);
                rectangle.Intersect(clippingRectangle);

                if (rectangle.Width > 0 && rectangle.Height > 0)
                {
                    rectangle.Offset(mapClientToScreen.X, mapClientToScreen.Y);
                    rectangles.Add(rectangle);
                }
            }

            // convert the list of rectangles into an array for returning
            Rectangle[] rectArray = new Rectangle[rectangles.Count];
            rectangles.CopyTo(rectArray);

            return _provider.RectArrayToDoubleArray(rectArray);
        }

        UnsafeNativeMethods.IRawElementProviderSimple UnsafeNativeMethods.ITextRangeProvider.GetEnclosingElement()
        {
            return _provider as UnsafeNativeMethods.IRawElementProviderSimple;
        }

        string UnsafeNativeMethods.ITextRangeProvider.GetText(int maxLength)
        {
            if (maxLength < 0)
            {
                maxLength = End;
            }

            string text = _provider.GetText();
            ValidateEndpoints();
            return text.Substring(Start, maxLength >= 0 ? Math.Min(Length, maxLength) : Length);
        }

        int UnsafeNativeMethods.ITextRangeProvider.Move(UnsafeNativeMethods.TextUnit unit, int count)
        {
            // Positive count means move forward. Negative count means move backwards.
            int moved = 0;
            if (count > 0)
            {
                // If the range is non-degenerate then we need to collapse the range.
                // (See the discussion of Count for ITextRange::Move)
                if (!IsDegenerate)
                {
                    // If the count is greater than zero, collapse the range at its end point.
                    Start = End;
                }

                // Move the degenerate range forward by the number of units
                int m;
                int start = Start;
                Start = MoveEndpointForward(Start, unit, count, out m);

                // If the start did not change then no move was done.
                if (start != Start)
                {
                    moved = m;
                }
            }
            else if (count < 0)
            {
                // If the range is non-degenerate then we need to collapse the range.
                if (!IsDegenerate)
                {
                    // If the count is less than zero, collapse the range at the starting point.
                    End = Start;
                }

                // Move the degenerate range backward by the number of units.
                int m;
                int end = End;
                End = MoveEndpointBackward(End, unit, count, out m);

                // If the end did not change then no move was done.
                if (end != End)
                {
                    moved = m;
                }
            }
            else
            {
                // Moving zero of any unit has no effect.
                moved = 0;
            }

            return moved;
        }

        int UnsafeNativeMethods.ITextRangeProvider.MoveEndpointByUnit(UnsafeNativeMethods.TextPatternRangeEndpoint endpoint, UnsafeNativeMethods.TextUnit unit, int count)
        {
            // Positive count means move forward. Negative count means move backwards.
            bool moveStart = endpoint == UnsafeNativeMethods.TextPatternRangeEndpoint.Start;

            int moved;

            int start = Start;
            int end = End;
            if (count > 0)
            {
                if (moveStart)
                {
                    Start = MoveEndpointForward(Start, unit, count, out moved);

                    // If the start did not change then no move was done.
                    if (start == Start)
                    {
                        moved = 0;
                    }
                }
                else
                {
                    End = MoveEndpointForward(End, unit, count, out moved);

                    // If the end did not change then no move was done.
                    if (end == End)
                    {
                        moved = 0;
                    }
                }
            }
            else if (count < 0)
            {
                if (moveStart)
                {
                    Start = MoveEndpointBackward(Start, unit, count, out moved);

                    // If the start did not change then no move was done.
                    if (start == Start)
                    {
                        moved = 0;
                    }
                }
                else
                {
                    End = MoveEndpointBackward(End, unit, count, out moved);

                    // If the end did not change then no move was done.
                    if (end == End)
                    {
                        moved = 0;
                    }
                }
            }
            else
            {
                // Moving zero of any unit has no effect.
                moved = 0;
            }

            return moved;
        }

        void UnsafeNativeMethods.ITextRangeProvider.MoveEndpointByRange(
            UnsafeNativeMethods.TextPatternRangeEndpoint endpoint,
            UnsafeNativeMethods.ITextRangeProvider targetRange,
            UnsafeNativeMethods.TextPatternRangeEndpoint targetEndpoint)
        {
            UiaTextRange textRange = (UiaTextRange)targetRange;
            int e = (targetEndpoint == UnsafeNativeMethods.TextPatternRangeEndpoint.Start)
                ? textRange.Start
                : textRange.End;

            if (endpoint == UnsafeNativeMethods.TextPatternRangeEndpoint.Start)
            {
                Start = e;
            }
            else
            {
                End = e;
            }
        }

        void UnsafeNativeMethods.ITextRangeProvider.Select()
        {
            _provider.SetSelection(Start, End);
        }

        void UnsafeNativeMethods.ITextRangeProvider.AddToSelection()
        {
            throw new InvalidOperationException();
        }

        void UnsafeNativeMethods.ITextRangeProvider.RemoveFromSelection()
        {
            throw new InvalidOperationException();
        }

        void UnsafeNativeMethods.ITextRangeProvider.ScrollIntoView(bool alignToTop)
        {
            if (_provider.IsMultiline)
            {
                int newFirstLine;

                if (alignToTop)
                {
                    newFirstLine = _provider.GetLineFromChar(Start);
                }
                else
                {
                    newFirstLine = Math.Max(0, _provider.GetLineFromChar(End) - _provider.LinesPerPage + 1);
                }

                _provider.LineScroll(Start, newFirstLine - _provider.GetFirstVisibleLine());

            }
            else if (_provider.IsScrollable)
            {
                int visibleStart;
                int visibleEnd;

                _provider.GetVisibleRangePoints(out visibleStart, out visibleEnd);

                if (_provider.IsReadingRTL(_provider.OwningControl.Handle))
                {
                    short key = UnsafeNativeMethods.VK_LEFT;

                    if (Start > visibleStart)
                    {
                        key = UnsafeNativeMethods.VK_RIGHT;
                    }

                    while (Start > visibleStart || Start < visibleEnd)
                    {
                        _provider.SendKeyboardInputVK(key, true);
                        _provider.GetVisibleRangePoints(out visibleStart, out visibleEnd);
                    }
                }
                else
                {
                    short key = UnsafeNativeMethods.VK_RIGHT;

                    if (Start < visibleStart)
                    {
                        key = UnsafeNativeMethods.VK_LEFT;
                    }

                    while (Start < visibleStart || Start > visibleEnd)
                    {
                        _provider.SendKeyboardInputVK(key, true);
                        _provider.GetVisibleRangePoints(out visibleStart, out visibleEnd);
                    }
                }
            }
        }

        #endregion Public Methods

        #region Public Properties

        UnsafeNativeMethods.IRawElementProviderSimple[] UnsafeNativeMethods.ITextRangeProvider.GetChildren()
        {
            // We don't have any children so return an empty array.
            return new UnsafeNativeMethods.IRawElementProviderSimple[0];
        }

        #endregion Public Properties

        #region Private Methods

        private static bool AtParagraphBoundary(string text, int index)
        {
            // Returns true if index identifies a paragraph boundary within text.
            return index <= 0 || index >= text.Length || (text[index - 1] == '\n') && (text[index] != '\n');
        }

        private static bool AtWordBoundary(string text, int index)
        {
            // Returns true if index identifies a word boundary within text.
            // Following richedit & word precedent the boundaries are at the leading edge of the word
            // so the span of a word includes trailing whitespace.

            // We are at a word boundary if we are at the beginning or end of the text.
            if (index <= 0 || index >= text.Length)
            {
                return true;
            }

            if (AtParagraphBoundary(text, index))
            {
                return true;
            }

            char ch1 = text[index - 1];
            char ch2 = text[index];

            // An apostrophe does *not* break a word if it follows or precedes characters.
            if ((char.IsLetterOrDigit(ch1) && IsApostrophe(ch2))
                || (IsApostrophe(ch1) && char.IsLetterOrDigit(ch2) && index >= 2 && char.IsLetterOrDigit(text[index - 2])))
            {
                return false;
            }

            // The following transitions mark boundaries.
            // Note: these are constructed to include trailing whitespace.
            return (char.IsWhiteSpace(ch1) && !char.IsWhiteSpace(ch2))
                || (char.IsLetterOrDigit(ch1) && !char.IsLetterOrDigit(ch2))
                || (!char.IsLetterOrDigit(ch1) && char.IsLetterOrDigit(ch2))
                || (char.IsPunctuation(ch1) && char.IsWhiteSpace(ch2));
        }

        private static bool IsApostrophe(char ch)
        {
            return ch == '\'' ||
                   ch == (char)0x2019; // Unicode Right Single Quote Mark
        }

        // TODO: Implement this.
        //private object GetAttributeValue(AutomationTextAttribute attribute)
        //{
        //    // A big pseudo-switch statement based on the attribute
        //
        //    object rval;
        //    if (attribute == TextPattern.BackgroundColorAttribute)
        //    {
        //        rval = GetBackgroundColor();
        //    }
        //    else if (attribute == TextPattern.CapStyleAttribute)
        //    {
        //        rval = GetCapStyle(_provider.WindowStyle);
        //    }
        //    else if (attribute == TextPattern.FontNameAttribute)
        //    {
        //        rval = GetFontName(_provider.GetLogfont());
        //    }
        //    else if (attribute == TextPattern.FontSizeAttribute)
        //    {
        //        rval = GetFontSize(_provider.GetLogfont());
        //    }
        //    else if (attribute == TextPattern.FontWeightAttribute)
        //    {
        //        rval = GetFontWeight(_provider.GetLogfont());
        //    }
        //    else if (attribute == TextPattern.ForegroundColorAttribute)
        //    {
        //        rval = GetForegroundColor();
        //    }
        //    else if (attribute == TextPattern.HorizontalTextAlignmentAttribute)
        //    {
        //        rval = GetHorizontalTextAlignment(_provider.WindowStyle);
        //    }
        //    else if (attribute == TextPattern.IsItalicAttribute)
        //    {
        //        rval = GetItalic(_provider.GetLogfont());
        //    }
        //    else if (attribute == TextPattern.IsReadOnlyAttribute)
        //    {
        //        rval = GetReadOnly();
        //    }
        //    else if (attribute == TextPattern.StrikethroughStyleAttribute)
        //    {
        //        rval = GetStrikethroughStyle(_provider.GetLogfont());
        //    }
        //    else if (attribute == TextPattern.UnderlineStyleAttribute)
        //    {
        //        rval = GetUnderlineStyle(_provider.GetLogfont());
        //    }
        //    else
        //    {
        //        rval = AutomationElement.NotSupported;
        //    }
        //    return rval;
        //}

        // helper function to accumulate a list of bounding rectangles for a potentially mult-line range
        private Collections.ArrayList GetMultilineBoundingRectangles(string text, Drawing.Point mapClientToScreen, Drawing.Rectangle clippingRectangle)
        {
            // Remember the line height.
            int height = Math.Abs(_provider.GetLogfont().lfHeight);

            // Get the starting and ending lines for the range.
            int start = Start;
            int end = End;

            int startLine = _provider.GetLineFromChar(start);
            int endLine = _provider.GetLineFromChar(end - 1);

            // Adjust the start based on the first visible line.
            int firstVisibleLine = _provider.GetFirstVisibleLine();
            if (firstVisibleLine > startLine)
            {
                startLine = firstVisibleLine;
                start = _provider.GetLineIndex(startLine);
            }

            // Adjust the end based on the last visible line.
            int lastVisibleLine = firstVisibleLine + _provider.GetLinesPerPage() - 1;
            if (lastVisibleLine < endLine)
            {
                endLine = lastVisibleLine;
                end = _provider.GetLineIndex(endLine) - 1;
            }

            // Adding a rectangle for each line.
            Collections.ArrayList rects = new Collections.ArrayList(Math.Max(endLine - startLine + 1, 0));
            int nextLineIndex = _provider.GetLineIndex(startLine);
            for (int i = startLine; i <= endLine; i++)
            {
                // Determine the starting coordinate on this line.
                Point startPoint;
                if (i == startLine)
                {
                    startPoint = _provider.GetPositionFromChar(start);
                }
                else
                {
                    startPoint = _provider.GetPositionFromChar(nextLineIndex);
                }

                // Determine the ending coordinate on this line.
                Point endPoint;
                if (i == endLine)
                {
                    endPoint = _provider.GetPositionFromCharUR(end - 1, text);
                }
                else
                {
                    nextLineIndex = _provider.GetLineIndex(i + 1);
                    endPoint = _provider.GetPositionFromChar(nextLineIndex - 1);
                }

                // Add a bounding rectangle for this line if it is nonempty.
                Rectangle rect = new Rectangle(startPoint.X, startPoint.Y, endPoint.X - startPoint.X, height);
                rect.Intersect(clippingRectangle);
                if (rect.Width > 0 && rect.Height > 0)
                {
                    rect.Offset(mapClientToScreen.X, mapClientToScreen.Y);
                    rects.Add(rect);
                }
            }

            return rects;
        }

        private object GetHorizontalTextAlignment(int style)
        {
            // Returns the value of the corresponding text attribute.
            if (_provider.IsBitSet(style, NativeMethods.ES_CENTER))
            {
                return HorizontalTextAlignment.Centered;
            }
            else if (_provider.IsBitSet(style, NativeMethods.ES_RIGHT))
            {
                return HorizontalTextAlignment.Right;
            }
            else
            {
                return HorizontalTextAlignment.Left;
            }
        }

        private object GetCapStyle(int style)
        {
            // Provides the value of the corresponding text attribute.
            return _provider.IsBitSet(style, NativeMethods.ES_UPPERCASE) ? CapStyle.AllCap : CapStyle.None;
        }

        private object GetReadOnly()
        {
            // Returns the value of the corresponding text attribute.
            return _provider.IsReadOnly;
        }

        private static object GetBackgroundColor()
        {
            // Returns the value of the corresponding text attribute.
            return SafeNativeMethods.GetSysColor(NativeMethods.COLOR_WINDOW);
        }

        private static object GetFontName(NativeMethods.LOGFONT logfont)
        {
            // Returns the value of the corresponding text attribute.
            return logfont.lfFaceName;
        }

        private object GetFontSize(NativeMethods.LOGFONT logfont)
        {
            // Returns the value of the corresponding text attribute.
            // Note: this assumes integral point sizes. violating this assumption would confuse the user
            // because they set something to 7 point but reports that it is, say 7.2 point, due to the rounding.
            IntPtr hdc = UiaTextProvider.GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero)
            {
                return null;
            }

            int lpy = Interop.Gdi32.GetDeviceCaps(hdc, Interop.Gdi32.DeviceCapability.LOGPIXELSY);
            UiaTextProvider.ReleaseDC(IntPtr.Zero, hdc);
            return Math.Round((double)(-logfont.lfHeight) * 72 / lpy);
        }

        private static object GetFontWeight(NativeMethods.LOGFONT logfont)
        {
            // Returns the value of the corresponding text attribute.
            return logfont.lfWeight;
        }

        private static object GetForegroundColor()
        {
            // Returns the value of the corresponding text attribute.
            return SafeNativeMethods.GetSysColor(NativeMethods.COLOR_WINDOWTEXT);
        }

        private static object GetItalic(NativeMethods.LOGFONT logfont)
        {
            // Returns the value of the corresponding text attribute.
            return logfont.lfItalic != 0;
        }

        private static object GetStrikethroughStyle(NativeMethods.LOGFONT logfont)
        {
            // Returns the value of the corresponding text attribute.
            return logfont.lfStrikeOut != 0 ? Automation.TextDecorationLineStyle.Single : Automation.TextDecorationLineStyle.None;
        }

        private static object GetUnderlineStyle(NativeMethods.LOGFONT logfont)
        {
            // Returns the value of the corresponding text attribute.
            return logfont.lfUnderline != 0 ? Automation.TextDecorationLineStyle.Single : Automation.TextDecorationLineStyle.None;
        }

        private int MoveEndpointForward(int index, UnsafeNativeMethods.TextUnit unit, int count, out int moved)
        {
            // Moves an endpoint forward a certain number of units.

            switch (unit)
            {
                case UnsafeNativeMethods.TextUnit.Character:
                    {
                        int limit = _provider.GetTextLength();
                        ValidateEndpoints();

                        moved = Math.Min(count, limit - index);
                        index = index + moved;

                        index = index > limit ? limit : index;
                    }
                    break;

                case UnsafeNativeMethods.TextUnit.Word:
                    {
                        string text = _provider.GetText();
                        ValidateEndpoints();

                        for (moved = 0; moved < count && index < text.Length; moved++)
                        {
                            index++;

                            while (!AtWordBoundary(text, index))
                            {
                                index++;
                            }
                        }
                    }
                    break;

                case UnsafeNativeMethods.TextUnit.Line:
                    {
                        // Figure out what line we are on. If we are in the middle of a line and
                        // are moving left then we'll round up to the next line so that we move
                        // to the beginning of the current line.
                        int line = _provider.GetLineFromChar(index);

                        // Limit the number of lines moved to the number of lines available to move
                        // Note lineMax is always >= 1.
                        int lineMax = _provider.GetLineCount();
                        moved = Math.Min(count, lineMax - line - 1);

                        if (moved > 0)
                        {
                            // move the endpoint to the beginning of the destination line.
                            index = _provider.GetLineIndex(line + moved);
                        }
                        else if (moved == 0 && lineMax == 1)
                        {
                            // There is only one line so get the text length as endpoint.
                            index = _provider.GetTextLength();
                            moved = 1;
                        }
                    }
                    break;

                case UnsafeNativeMethods.TextUnit.Paragraph:
                    {
                        // Just like moving words but we look for paragraph boundaries instead of 
                        // word boundaries.
                        string text = _provider.GetText();
                        ValidateEndpoints();

                        for (moved = 0; moved < count && index < text.Length; moved++)
                        {
                            index++;

                            while(!AtParagraphBoundary(text, index))
                            {
                                index++;
                            }
                        }
                    }
                    break;

                case UnsafeNativeMethods.TextUnit.Format:
                case UnsafeNativeMethods.TextUnit.Page:
                case UnsafeNativeMethods.TextUnit.Document:
                    {
                        // Since edit controls are plain text moving one uniform format unit will
                        // take us all the way to the end of the document, just like
                        // "pages" and document.
                        int limit = _provider.GetTextLength();
                        ValidateEndpoints();

                        // We'll move 1 format unit if we aren't already at the end of the
                        // document.  Otherwise, we won't move at all.
                        moved = index < limit ? 1 : 0;
                        index = limit;
                    }
                    break;

                default:
                    throw new InvalidEnumArgumentException("unit", (int)unit, typeof(UnsafeNativeMethods.TextUnit));
            }

            return index;
        }

        private int MoveEndpointBackward(int index, UnsafeNativeMethods.TextUnit unit, int count, out int moved)
        {
            // Moves an endpoint backward a certain number of units.

            switch (unit)
            {
                case UnsafeNativeMethods.TextUnit.Character:
                    {
                        int limit = _provider.GetTextLength();
                        ValidateEndpoints();

                        int oneBasedIndex = index + 1;

                        moved = Math.Max(count, -oneBasedIndex);
                        index = index + moved;

                        index = index < 0 ? 0 : index;
                    }
                    break;

                case UnsafeNativeMethods.TextUnit.Word:
                    {
                        string text = _provider.GetText();
                        ValidateEndpoints();

                        for (moved = 0; moved > count && index > 0; moved--)
                        {
                            index--;

                            while (!AtWordBoundary(text, index))
                            {
                                index--;
                            }
                        }
                    }
                    break;

                case UnsafeNativeMethods.TextUnit.Line:
                    {
                        // Note count < 0.

                        // Get 1-based line.
                        int line = _provider.GetLineFromChar(index) + 1;

                        int lineMax = _provider.GetLineCount();

                        // Truncate the count to the number of available lines.
                        int actualCount = Math.Max(count, -line);

                        moved = actualCount;

                        if (actualCount == -line)
                        {
                            // We are moving by the maximum number of possible lines,
                            // so we know the resulting index will be 0.
                            index = 0;

                            // If a line other than the first consists of only "\r\n",
                            // you can move backwards past this line and the position changes,
                            // hence this is counted.  The first line is special, though:
                            // if it is empty, and you move say from the second line back up
                            // to the first, you cannot move further; however if the first line
                            // is nonempty, you can move from the end of the first line to its
                            // beginning!  This latter move is counted, but if the first line
                            // is empty, it is not counted.

                            // Recalculate the value of "moved".
                            // The first line is empty if it consists only of
                            // a line separator sequence.
                            bool firstLineEmpty =
                                ((lineMax > 1 && _provider.GetLineIndex(1) == _lineSeparator.Length)
                                    || lineMax == 0);

                            if (moved < 0 && firstLineEmpty)
                            {
                                ++moved;
                            }
                        }
                        else // actualCount > -line
                        {
                            // Move the endpoint to the beginning of the following line,
                            // then back by the line separator length to get to the end
                            // of the previous line, since the Edit control has
                            // no method to get the character index of the end
                            // of a line directly.
                            index = _provider.GetLineIndex(line + actualCount) - _lineSeparator.Length;
                        }
                    }
                    break;

                case UnsafeNativeMethods.TextUnit.Paragraph:
                    {
                        // Just like moving words but we look for paragraph boundaries instead of 
                        // word boundaries.
                        string text = _provider.GetText();
                        ValidateEndpoints();

                        for (moved = 0; moved > count && index > 0; moved--)
                        {
                            index--;
                            while (!AtParagraphBoundary(text, index))
                            {
                                index--;
                            }
                        }
                    }
                    break;

                case UnsafeNativeMethods.TextUnit.Format:
                case UnsafeNativeMethods.TextUnit.Page:
                case UnsafeNativeMethods.TextUnit.Document:
                    {
                        // Since edit controls are plain text moving one uniform format unit will
                        // take us all the way to the beginning of the document, just like
                        // "pages" and document.

                        // We'll move 1 format unit if we aren't already at the beginning of the
                        // document.  Otherwise, we won't move at all.
                        moved = index > 0 ? -1 : 0;
                        index = 0;
                    }
                    break;

                default:
                    throw new InvalidEnumArgumentException("unit", (int)unit, typeof(UnsafeNativeMethods.TextUnit));
            }

            return index;
        }

        private void MoveTo(int start, int end)
        {
            // Method to set both endpoints simultaneously.

            if (start < 0 || end < start)
            {
                throw new InvalidOperationException("Character offset not valid in TextRange.");
            }

            _start = start;
            _end = end;
        }

        private void ValidateEndpoints()
        {
            int limit = _provider.GetTextLength();
            if (Start > limit || End > limit)
            {
                throw new InvalidOperationException("Start or end specified is past the end of the text range.");
            }
        }

        #endregion Private Methods

        #region Private Properties

        private bool IsDegenerate
        {
            get
            {
                // Strictly only needs to be == since never should _start > _end.
                return _start >= _end;
            }
        }

        private int End
        {
            get
            {
                return _end;
            }
            set
            {
                // ensure that we never accidentally get a negative index
                if (value < 0)
                {
                    throw new InvalidOperationException("Character offset not valid in TextRange.");
                }

                // ensure that end never moves before start
                if (value < _start)
                {
                    _start = value;
                }
                _end = value;
            }
        }

        private int Length
        {
            get
            {
                return _end - _start;
            }
        }

        private int Start
        {
            get
            {
                return _start;
            }
            set
            {
                // Ensure that we never accidentally get a negative index.
                if (value < 0)
                {
                    throw new InvalidOperationException("Character offset not valid in TextRange.");
                }

                // Ensure that start never moves after end.
                if (value > _end)
                {
                    _end = value;
                }

                _start = value;
            }
        }

        #endregion Private Properties

        #region Private Fields

        private UiaTextProvider _provider;
        private int _start;
        private int _end;

        // Edit controls always use "\r\n" as the line separator, not "\n".
        private const string _lineSeparator = "\r\n";  // This string is a non-localizable string

        #endregion Private Fields
    }
}
