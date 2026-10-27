using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Yata.Views;

/// <summary>A minimal "− value +" number picker; no typing, so no invalid input.</summary>
public partial class Stepper : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(Stepper),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((Stepper)d).Refresh()));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(int), typeof(Stepper), new PropertyMetadata(0, (d, _) => ((Stepper)d).Refresh()));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(int), typeof(Stepper), new PropertyMetadata(100, (d, _) => ((Stepper)d).Refresh()));

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(int), typeof(Stepper), new PropertyMetadata(1));

    public static readonly DependencyProperty SuffixProperty = DependencyProperty.Register(
        nameof(Suffix), typeof(string), typeof(Stepper), new PropertyMetadata("", (d, _) => ((Stepper)d).Refresh()));

    /// <summary>What the number is, for screen readers ("Diminuir Pausa curta, 5 min").</summary>
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(Stepper), new PropertyMetadata("", (d, _) => ((Stepper)d).Refresh()));

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int Minimum
    {
        get => (int)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public int Step
    {
        get => (int)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public string Suffix
    {
        get => (string)GetValue(SuffixProperty);
        set => SetValue(SuffixProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public Stepper()
    {
        InitializeComponent();
        Refresh();
    }

    private void OnMinus(object sender, RoutedEventArgs e) => Value = Math.Max(Minimum, Value - Step);

    private void OnPlus(object sender, RoutedEventArgs e) => Value = Math.Min(Maximum, Value + Step);

    private void Refresh()
    {
        if (ValueText == null)
            return;
        ValueText.Text = Value + Suffix;
        // At a limit the button turns off; keep keyboard focus on the one that still works.
        if (Minus.IsKeyboardFocused && Value <= Minimum)
            Plus.Focus();
        else if (Plus.IsKeyboardFocused && Value >= Maximum)
            Minus.Focus();
        Minus.IsEnabled = Value > Minimum;
        Plus.IsEnabled = Value < Maximum;

        string what = Label.Length > 0 ? $"{Label}, " : "";
        AutomationProperties.SetName(Minus, $"Diminuir {what}{ValueText.Text}");
        AutomationProperties.SetName(Plus, $"Aumentar {what}{ValueText.Text}");
    }
}
