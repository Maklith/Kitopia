using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Utils;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Media.TextFormatting.Unicode;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Utilities;

namespace KitopiaEx.Ocr;
internal static class StringUtils
  {
    public static bool IsEol(char c) => c == '\r' || c == '\n';

    public static bool IsStartOfWord(string text, int index)
    {
      if (index >= text.Length)
        return false;
      Codepoint codepoint1 = new Codepoint((uint) text[index]);
      if (index > 0)
      {
        Codepoint codepoint2 = new Codepoint((uint) text[index - 1]);
        if (!codepoint2.IsWhiteSpace)
          return false;
        if (codepoint2.IsBreakChar)
          return true;
      }
      switch (codepoint1.GeneralCategory)
      {
        case GeneralCategory.LowercaseLetter:
        case GeneralCategory.TitlecaseLetter:
        case GeneralCategory.UppercaseLetter:
        case GeneralCategory.DecimalNumber:
        case GeneralCategory.LetterNumber:
        case GeneralCategory.OtherNumber:
        case GeneralCategory.DashPunctuation:
        case GeneralCategory.InitialPunctuation:
        case GeneralCategory.OpenPunctuation:
        case GeneralCategory.CurrencySymbol:
        case GeneralCategory.MathSymbol:
          return true;
        default:
          return false;
      }
    }

    public static bool IsEndOfWord(string text, int index)
    {
      if (index >= text.Length)
        return true;
      Codepoint codepoint = new Codepoint((uint) text[index]);
      if (!codepoint.IsWhiteSpace)
        return false;
      if (index > 0 && (index + 1 >= text.Length || new Codepoint((uint) text[index + 1]).IsBreakChar))
        return true;
      switch (codepoint.GeneralCategory)
      {
        case GeneralCategory.LowercaseLetter:
        case GeneralCategory.TitlecaseLetter:
        case GeneralCategory.UppercaseLetter:
        case GeneralCategory.DecimalNumber:
        case GeneralCategory.LetterNumber:
        case GeneralCategory.OtherNumber:
        case GeneralCategory.DashPunctuation:
        case GeneralCategory.InitialPunctuation:
        case GeneralCategory.OpenPunctuation:
        case GeneralCategory.CurrencySymbol:
        case GeneralCategory.MathSymbol:
          return false;
        default:
          return true;
      }
    }

    public static int PreviousWord(string text, int cursor)
    {
      if (string.IsNullOrEmpty(text))
        return 0;
      cursor = Math.Min(cursor, text.Length);
      int index = StringUtils.LineBegin(text, cursor) - 1;
      int num1 = index <= 0 || text[index] != '\n' || text[index - 1] != '\r' ? index : index - 1;
      if (cursor - 1 == index)
        return num1 <= 0 ? 0 : num1;
      StringUtils.CharClass charClass1 = StringUtils.GetCharClass(text[cursor - 1]);
      int num2 = index + 1;
      int num3 = cursor;
      while (num3 > num2 && StringUtils.GetCharClass(text[num3 - 1]) == charClass1)
        --num3;
      if (charClass1 == StringUtils.CharClass.CharClassWhitespace && num3 > num2)
      {
        StringUtils.CharClass charClass2 = StringUtils.GetCharClass(text[num3 - 1]);
        while (num3 > num2 && StringUtils.GetCharClass(text[num3 - 1]) == charClass2)
          --num3;
      }
      return num3;
    }

    public static int NextWord(string text, int cursor)
    {
      int index1 = StringUtils.LineEnd(text, cursor);
      if (cursor >= text.Length)
        return cursor;
      int num = index1 >= text.Length || text[index1] != '\r' || index1 + 1 >= text.Length || text[index1 + 1] != '\n' ? index1 : index1 + 1;
      if (cursor == index1 || cursor == num)
        return num < text.Length ? num + 1 : cursor;
      int index2 = cursor;
      while (index2 < index1 && char.IsWhiteSpace(text[index2]))
        ++index2;
      if (index2 >= index1)
        return index2;
      StringUtils.CharClass charClass = StringUtils.GetCharClass(text[index2]);
      while (index2 < index1 && StringUtils.GetCharClass(text[index2]) == charClass)
        ++index2;
      return index2;
    }

    private static StringUtils.CharClass GetCharClass(char c)
    {
      if (char.IsWhiteSpace(c))
        return StringUtils.CharClass.CharClassWhitespace;
      return char.IsLetterOrDigit(c) ? StringUtils.CharClass.CharClassAlphaNumeric : StringUtils.CharClass.CharClassUnknown;
    }

    private static int LineBegin(string text, int pos)
    {
      while (pos > 0 && !StringUtils.IsEol(text[pos - 1]))
        --pos;
      return pos;
    }

    private static int LineEnd(string text, int cursor, bool include = false)
    {
      while (cursor < text.Length && !StringUtils.IsEol(text[cursor]))
        ++cursor;
      if (include && cursor < text.Length)
      {
        if (text[cursor] == '\r' && text[cursor + 1] == '\n')
          cursor += 2;
        else
          ++cursor;
      }
      return cursor;
    }

    private enum CharClass
    {
      CharClassUnknown,
      CharClassWhitespace,
      CharClassAlphaNumeric,
    }
  }
public partial class AdaptiveTextBox : TextBlock
{
    public Point TopLeft
    {
        get => GetValue(TopLeftProperty);
        set => SetValue(TopLeftProperty, value);
    }

    public static readonly StyledProperty<Point> TopLeftProperty =
        AvaloniaProperty.Register<AdaptiveTextBox, Point>(nameof(TopLeft));

    public Point BottomRight
    {
        get => GetValue(BottomRightProperty);
        set => SetValue(BottomRightProperty, value);
    }

    public static readonly StyledProperty<Point> BottomRightProperty =
        AvaloniaProperty.Register<AdaptiveTextBox, Point>(nameof(BottomRight));
    public static readonly StyledProperty<int> SelectionStartProperty = TextBox.SelectionStartProperty.AddOwner<AdaptiveTextBox>();
    public static readonly StyledProperty<int> SelectionEndProperty = TextBox.SelectionEndProperty.AddOwner<AdaptiveTextBox>();
    public static readonly DirectProperty<AdaptiveTextBox, string> SelectedTextProperty = AvaloniaProperty.RegisterDirect<AdaptiveTextBox, string>(nameof (SelectedText), (Func<AdaptiveTextBox, string>) (o => o.SelectedText));
    public static readonly StyledProperty<IBrush?> SelectionBrushProperty = TextBox.SelectionBrushProperty.AddOwner<AdaptiveTextBox>();
    public static readonly StyledProperty<IBrush?> SelectionForegroundBrushProperty = TextBox.SelectionForegroundBrushProperty.AddOwner<AdaptiveTextBox>();
    public static readonly DirectProperty<AdaptiveTextBox, bool> CanCopyProperty = TextBox.CanCopyProperty.AddOwner<AdaptiveTextBox>((Func<AdaptiveTextBox, bool>) (o => o.CanCopy));
    public static readonly RoutedEvent<RoutedEventArgs> CopyingToClipboardEvent = RoutedEvent.Register<AdaptiveTextBox, RoutedEventArgs>("CopyingToClipboard", RoutingStrategies.Bubble);
    private bool _canCopy;
    private int _wordSelectionStart = -1;

    static AdaptiveTextBox()
    {
      Dispatcher.UIThread.InvokeAsync(() =>
      {
        InputElement.FocusableProperty.OverrideDefaultValue(typeof(AdaptiveTextBox), true);
        Visual.AffectsRender<AdaptiveTextBox>((AvaloniaProperty)AdaptiveTextBox.SelectionStartProperty,
          (AvaloniaProperty)AdaptiveTextBox.SelectionEndProperty,
          (AvaloniaProperty)AdaptiveTextBox.SelectionBrushProperty);
        BackgroundProperty.OverrideDefaultValue<AdaptiveTextBox>(new SolidColorBrush(Colors.Gray, 0.7d));
        ForegroundProperty.OverrideDefaultValue<AdaptiveTextBox>(new SolidColorBrush(Colors.White));
      });
      // FocusableProperty.OverrideDefaultValue<AdaptiveTextBox>(false);
    }

    public AdaptiveTextBox()
    {
      
    }

    protected override void OnInitialized()
    {
      base.OnInitialized();
      
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
     
     
      base.OnLoaded(e);
    }

    public override void ApplyTemplate()
    {
      base.ApplyTemplate();
      Focusable = false;
      
      double width = Math.Abs(BottomRight.X - TopLeft.X);
      double height = Math.Abs(BottomRight.Y - TopLeft.Y);
      SelectionBrush=new SolidColorBrush(Colors.Cyan,0.7d);

      Width = width;
      Height = height;
      HorizontalAlignment = HorizontalAlignment.Left;
      VerticalAlignment = VerticalAlignment.Top;
      TextAlignment = TextAlignment.Center;
      TextWrapping = TextWrapping.NoWrap;


      Foreground = new SolidColorBrush(Colors.White);
      double targetSize = height / 1.5;
      
      var availableSize = new Size(double.PositiveInfinity, double.PositiveInfinity);
      var textBlock = new TextBlock
      {
        Text = Text,
        FontSize =  targetSize
      
      };
      // 测量 TextBlock
      textBlock.Measure(availableSize);
      while (textBlock.DesiredSize.Width>width)
      {
        targetSize -=1;
        textBlock.FontSize = targetSize;
        textBlock.Measure(availableSize);
      }

      this.FontSize = targetSize;
    }

    public event EventHandler<RoutedEventArgs>? CopyingToClipboard
    {
      add => this.AddHandler<RoutedEventArgs>(AdaptiveTextBox.CopyingToClipboardEvent, value);
      remove
      {
        this.RemoveHandler<RoutedEventArgs>(AdaptiveTextBox.CopyingToClipboardEvent, value);
      }
    }

    /// <summary>Gets or sets the brush that highlights selected text.</summary>
    public IBrush? SelectionBrush
    {
      get => this.GetValue<IBrush>(AdaptiveTextBox.SelectionBrushProperty);
      set => this.SetValue<IBrush>(AdaptiveTextBox.SelectionBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets a brush that is used for the foreground of selected text
    /// </summary>
    public IBrush? SelectionForegroundBrush
    {
      get => this.GetValue<IBrush>(AdaptiveTextBox.SelectionForegroundBrushProperty);
      set => this.SetValue<IBrush>(AdaptiveTextBox.SelectionForegroundBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets a character index for the beginning of the current selection.
    /// </summary>
    public int SelectionStart
    {
      get => this.GetValue<int>(AdaptiveTextBox.SelectionStartProperty);
      set => this.SetValue<int>(AdaptiveTextBox.SelectionStartProperty, value);
    }

    /// <summary>
    /// Gets or sets a character index for the end of the current selection.
    /// </summary>
    public int SelectionEnd
    {
      get => this.GetValue<int>(AdaptiveTextBox.SelectionEndProperty);
      set => this.SetValue<int>(AdaptiveTextBox.SelectionEndProperty, value);
    }

    /// <summary>Gets the content of the current selection.</summary>
    public string SelectedText => this.GetSelection();

    /// <summary>
    /// Property for determining if the Copy command can be executed.
    /// </summary>
    public bool CanCopy
    {
      get => this._canCopy;
      private set
      {
        this.SetAndRaise<bool>((DirectPropertyBase<bool>) AdaptiveTextBox.CanCopyProperty, ref this._canCopy, value);
      }
    }

    /// <summary>Copies the current selection to the Clipboard.</summary>
    public async Task Copy()
    {
      AdaptiveTextBox AdaptiveTextBox = this;
      if (!AdaptiveTextBox._canCopy)
        return;
      string selection = AdaptiveTextBox.GetSelection();
      if (string.IsNullOrEmpty(selection))
        return;
      RoutedEventArgs e = new RoutedEventArgs((RoutedEvent) AdaptiveTextBox.CopyingToClipboardEvent);
      // ISSUE: explicit non-virtual call
      AdaptiveTextBox.RaiseEvent(e);
      if (e.Handled)
        return;
      IClipboard clipboard = TopLevel.GetTopLevel((Visual) AdaptiveTextBox)?.Clipboard;
      if (clipboard == null)
        return;
      await clipboard.SetTextAsync(selection);
    }

    /// <summary>Select all text in the TextBox</summary>
    public void SelectAll()
    {
      string text = this.Text;
      this.SetCurrentValue<int>(AdaptiveTextBox.SelectionStartProperty, 0);
      this.SetCurrentValue<int>(AdaptiveTextBox.SelectionEndProperty, text != null ? text.Length : 0);
    }

    /// <summary>Clears the current selection</summary>
    public void ClearSelection()
    {
      this.SetCurrentValue<int>(AdaptiveTextBox.SelectionEndProperty, this.SelectionStart);
    }

    internal void SelectText(Point start,Point end)
    {
      string text = Text;
      Thickness padding = this.Padding;
      //Point point = end - new Point(padding.Left, padding.Top);
      //point = new Point(MathUtilities.Clamp(point.X, 0.0, Math.Max(this.TextLayout.WidthIncludingTrailingWhitespace, 0.0)), MathUtilities.Clamp(point.Y, 0.0, Math.Max(this.TextLayout.Height, 0.0)));
      int textPosition = this.TextLayout.HitTestPoint(in end).TextPosition;
     
      this.SetCurrentValue<int>(SelectableTextBlock.SelectionEndProperty, textPosition);
      int textPosition2 = this.TextLayout.HitTestPoint(in start).TextPosition;
     
      this.SetCurrentValue<int>(SelectableTextBlock.SelectionStartProperty, textPosition2);
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
      
      
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
      
    }

  

    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
      int selectionStart = this.SelectionStart;
      int selectionEnd = this.SelectionEnd;
      IBrush selectionBrush = this.SelectionBrush;
      if (selectionStart != selectionEnd && selectionBrush != null)
      {
        int start = Math.Min(selectionStart, selectionEnd);
        int length = Math.Max(selectionStart, selectionEnd) - start;
        IEnumerable<Rect> rects = this.TextLayout.HitTestTextRange(start, length);
        using (context.PushTransform(Matrix.CreateTranslation((Vector) origin)))
        {
          foreach (Rect rect in rects)
            context.FillRectangle(selectionBrush, PixelRect.FromRect(rect, 1.0).ToRect(1.0));
        }
      }
      base.RenderTextLayout(context, origin);
    }

    internal void SetPointerIsHover()
    {
      PseudoClasses.Add(":pointerover");
    }
    internal void SetPointerIsNotHover()
    {
      PseudoClasses.Remove(":pointerover");
    }

   
    protected override void OnKeyDown(KeyEventArgs e)
    {
     
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
     
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
     
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
     
     
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
 
    }

    private void UpdateCommandStates() => this.CanCopy = !string.IsNullOrEmpty(this.GetSelection());

    private string GetSelection()
    {
      string str =Text;
      int length1 = str != null ? str.Length : 0;
      if (length1 == 0)
        return "";
      int selectionStart = this.SelectionStart;
      int selectionEnd = this.SelectionEnd;
      int startIndex = Math.Min(selectionStart, selectionEnd);
      int num = Math.Max(selectionStart, selectionEnd);
      if (startIndex == num || length1 < num)
        return "";
      int length2 = Math.Max(0, num - startIndex);
      return str.Substring(startIndex, length2);
    }
  }

    
