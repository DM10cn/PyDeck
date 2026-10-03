using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Runtime.CompilerServices;

namespace PimGui.App;

// WinUI Button/ToggleSwitch remain responsible for pointer, keyboard, command and UI Automation
// semantics. These templates replace their visuals; they do not emulate controls with click handlers.
internal static class MaterialTemplates
{
    private static readonly ConditionalWeakTable<ToggleSwitch, object> switchContentTracking = new();

    private static void UpdateSwitchContentSpacing(DependencyObject sender, DependencyProperty? property = null)
    {
        if (sender is not ToggleSwitch toggle || VisualTreeHelper.GetChildrenCount(toggle) == 0 ||
            VisualTreeHelper.GetChild(toggle, 0) is not Panel templateRoot ||
            templateRoot.Children.OfType<Grid>().FirstOrDefault(child => child.Name == "SwitchLayout") is not { } layout) return;
        static bool HasContent(object? content) => content is not null && (content is not string text || text.Length > 0);
        // Empty on/off content is common for labeled settings rows. Its native
        // content presenters stay in the template, but reserve no trailing gap.
        layout.ColumnDefinitions[1].Width = new(HasContent(toggle.OnContent) || HasContent(toggle.OffContent) ? 12 : 0);
    }

    public static void UpdateMotion(DependencyObject root, Duration duration)
    {
        if (root is FrameworkElement { Tag: "MaterialMotionRoot" } templateRoot)
        {
            if (VisualTreeHelper.GetParent(templateRoot) is ToggleSwitch toggle)
            {
                if (!switchContentTracking.TryGetValue(toggle, out _))
                {
                    switchContentTracking.Add(toggle, new());
                    toggle.RegisterPropertyChangedCallback(Microsoft.UI.Xaml.Controls.ToggleSwitch.OnContentProperty, UpdateSwitchContentSpacing);
                    toggle.RegisterPropertyChangedCallback(Microsoft.UI.Xaml.Controls.ToggleSwitch.OffContentProperty, UpdateSwitchContentSpacing);
                }
                UpdateSwitchContentSpacing(toggle);
            }
            if (templateRoot is Panel panel && panel.Children.OfType<Grid>().Any(child => child.Name == "RippleHost"))
            {
                DependencyObject? owner = VisualTreeHelper.GetParent(templateRoot);
                while (owner is not null && owner is not Control) owner = VisualTreeHelper.GetParent(owner);
                if (owner is Control control) MaterialRipple.Configure(control, duration.TimeSpan > TimeSpan.Zero);
            }
            foreach (var group in VisualStateManager.GetVisualStateGroups(templateRoot))
            {
                foreach (var transition in group.Transitions) transition.GeneratedDuration = duration;
                // Keep the live template and its focused control. Settling the current
                // state does not dispatch any command or rebuild the business page.
                if (duration.TimeSpan == TimeSpan.Zero && group.CurrentState is { } current)
                {
                    DependencyObject? parent = VisualTreeHelper.GetParent(templateRoot);
                    while (parent is not null && parent is not Control) parent = VisualTreeHelper.GetParent(parent);
                    if (parent is Control control) VisualStateManager.GoToState(control, current.Name, false);
                }
            }
        }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            UpdateMotion(VisualTreeHelper.GetChild(root, index), duration);
    }

    public static string Button() => """
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Setter Property="UseSystemFocusVisuals" Value="True" />
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="Button">
                <Grid x:Name="Root" Background="Transparent" Tag="MaterialMotionRoot">
                  <VisualStateManager.VisualStateGroups>
                    <VisualStateGroup x:Name="CommonStates">
                      <VisualStateGroup.Transitions>
                        <VisualTransition GeneratedDuration="0:0:0" />
                      </VisualStateGroup.Transitions>
                      <VisualState x:Name="Normal" />
                      <VisualState x:Name="PointerOver">
                        <VisualState.Setters>
                          <Setter Target="StateLayer.Opacity" Value="{ThemeResource PyDeckHoverOpacity}" />
                        </VisualState.Setters>
                      </VisualState>
                      <VisualState x:Name="Pressed">
                        <VisualState.Setters>
                          <Setter Target="StateLayer.Opacity" Value="{ThemeResource PyDeckPressedOpacity}" />
                          <Setter Target="ButtonBorder.CornerRadius" Value="{ThemeResource PyDeckActionPressedCornerRadius}" />
                          <Setter Target="StateLayer.CornerRadius" Value="{ThemeResource PyDeckActionPressedCornerRadius}" />
                        </VisualState.Setters>
                      </VisualState>
                      <VisualState x:Name="Disabled">
                        <VisualState.Setters>
                          <Setter Target="ButtonBorder.Background" Value="{ThemeResource PyDeckActionDisabledBackground}" />
                          <Setter Target="ButtonBorder.BorderBrush" Value="{ThemeResource PyDeckActionDisabledOutline}" />
                          <Setter Target="ContentPresenter.Foreground" Value="{ThemeResource PyDeckActionDisabledForeground}" />
                        </VisualState.Setters>
                      </VisualState>
                    </VisualStateGroup>
                    <VisualStateGroup x:Name="FocusStates">
                      <VisualState x:Name="Focused" />
                      <VisualState x:Name="PointerFocused" />
                      <VisualState x:Name="Unfocused" />
                    </VisualStateGroup>
                  </VisualStateManager.VisualStateGroups>
                  <Border x:Name="ButtonBorder" Background="{TemplateBinding Background}" BackgroundSizing="OuterBorderEdge"
                          BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"
                          CornerRadius="{TemplateBinding CornerRadius}" />
                  <Border x:Name="StateLayer" Background="{ThemeResource PyDeckActionStateLayer}" Opacity="0"
                          CornerRadius="{TemplateBinding CornerRadius}" IsHitTestVisible="False" />
                  <Grid x:Name="RippleHost" IsHitTestVisible="False" AutomationProperties.AccessibilityView="Raw" />
                  <ContentPresenter x:Name="ContentPresenter" Content="{TemplateBinding Content}"
                          ContentTemplate="{TemplateBinding ContentTemplate}" ContentTransitions="{TemplateBinding ContentTransitions}"
                          Padding="{TemplateBinding Padding}" Foreground="{TemplateBinding Foreground}"
                          HorizontalContentAlignment="{TemplateBinding HorizontalContentAlignment}"
                          VerticalContentAlignment="{TemplateBinding VerticalContentAlignment}"
                          AutomationProperties.AccessibilityView="Raw" />
                </Grid>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """;

    public static string ToggleSwitch() => """
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ToggleSwitch">
          <Setter Property="HorizontalAlignment" Value="Left" />
          <Setter Property="VerticalAlignment" Value="Center" />
          <Setter Property="HorizontalContentAlignment" Value="Left" />
          <Setter Property="VerticalContentAlignment" Value="Center" />
          <Setter Property="ManipulationMode" Value="System,TranslateX" />
          <Setter Property="UseSystemFocusVisuals" Value="True" />
          <Setter Property="MinHeight" Value="44" />
          <Setter Property="FocusVisualMargin" Value="-4,-2,-4,-2" />
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="ToggleSwitch">
                <Grid Background="{TemplateBinding Background}" Tag="MaterialMotionRoot">
                  <VisualStateManager.VisualStateGroups>
                    <VisualStateGroup x:Name="CommonStates">
                      <!-- Press/hover feedback is immediate; only the native knob
                           movement in ToggleStates interpolates between values. -->
                      <VisualState x:Name="Normal" />
                      <VisualState x:Name="PointerOver">
                        <VisualState.Setters>
                          <Setter Target="StateLayer.Opacity" Value="{ThemeResource PyDeckHoverOpacity}" />
                          <Setter Target="OuterBorder.Fill" Value="{ThemeResource ToggleSwitchFillOffPointerOver}" />
                          <Setter Target="OuterBorder.Stroke" Value="{ThemeResource ToggleSwitchStrokeOffPointerOver}" />
                          <Setter Target="SwitchKnobBounds.Fill" Value="{ThemeResource ToggleSwitchFillOnPointerOver}" />
                          <Setter Target="SwitchKnobBounds.Stroke" Value="{ThemeResource ToggleSwitchStrokeOnPointerOver}" />
                          <Setter Target="SwitchKnobOff.Fill" Value="{ThemeResource ToggleSwitchKnobFillOffPointerOver}" />
                          <Setter Target="SwitchKnobOn.Fill" Value="{ThemeResource ToggleSwitchKnobFillOnPointerOver}" />
                        </VisualState.Setters>
                      </VisualState>
                      <VisualState x:Name="Pressed">
                        <VisualState.Setters>
                          <Setter Target="StateLayer.Opacity" Value="{ThemeResource PyDeckPressedOpacity}" />
                          <Setter Target="OuterBorder.Fill" Value="{ThemeResource ToggleSwitchFillOffPressed}" />
                          <Setter Target="SwitchKnobBounds.Fill" Value="{ThemeResource ToggleSwitchFillOnPressed}" />
                          <Setter Target="SwitchKnobOn.Width" Value="28" />
                          <Setter Target="SwitchKnobOn.Height" Value="28" />
                          <Setter Target="SwitchKnobOff.Width" Value="28" />
                          <Setter Target="SwitchKnobOff.Height" Value="28" />
                        </VisualState.Setters>
                      </VisualState>
                      <VisualState x:Name="Disabled">
                        <VisualState.Setters>
                          <Setter Target="OuterBorder.Fill" Value="{ThemeResource ToggleSwitchFillOffDisabled}" />
                          <Setter Target="OuterBorder.Stroke" Value="{ThemeResource ToggleSwitchStrokeOffDisabled}" />
                          <Setter Target="SwitchKnobBounds.Fill" Value="{ThemeResource ToggleSwitchFillOnDisabled}" />
                          <Setter Target="SwitchKnobBounds.Stroke" Value="{ThemeResource ToggleSwitchStrokeOnDisabled}" />
                          <Setter Target="SwitchKnobOff.Fill" Value="{ThemeResource ToggleSwitchKnobFillOffDisabled}" />
                          <Setter Target="SwitchKnobOn.Fill" Value="{ThemeResource ToggleSwitchKnobFillOnDisabled}" />
                          <Setter Target="OffContentPresenter.Foreground" Value="{ThemeResource ToggleSwitchContentForegroundDisabled}" />
                          <Setter Target="OnContentPresenter.Foreground" Value="{ThemeResource ToggleSwitchContentForegroundDisabled}" />
                          <Setter Target="HeaderContentPresenter.Foreground" Value="{ThemeResource ToggleSwitchContentForegroundDisabled}" />
                        </VisualState.Setters>
                      </VisualState>
                    </VisualStateGroup>
                    <VisualStateGroup x:Name="ToggleStates">
                      <VisualStateGroup.Transitions>
                        <VisualTransition From="Off" To="On" GeneratedDuration="0:0:0" />
                        <VisualTransition From="On" To="Off" GeneratedDuration="0:0:0" />
                        <VisualTransition From="Dragging" To="On" GeneratedDuration="0:0:0" />
                        <VisualTransition From="Dragging" To="Off" GeneratedDuration="0:0:0" />
                      </VisualStateGroup.Transitions>
                      <VisualState x:Name="Dragging" />
                      <VisualState x:Name="Off" />
                      <VisualState x:Name="On">
                        <Storyboard>
                          <DoubleAnimation Storyboard.TargetName="KnobTranslateTransform" Storyboard.TargetProperty="X" To="20" Duration="0" />
                          <DoubleAnimation Storyboard.TargetName="SwitchKnobBounds" Storyboard.TargetProperty="Opacity" To="1" Duration="0" />
                          <DoubleAnimation Storyboard.TargetName="OuterBorder" Storyboard.TargetProperty="Opacity" To="0" Duration="0" />
                          <DoubleAnimation Storyboard.TargetName="SwitchKnobOn" Storyboard.TargetProperty="Opacity" To="1" Duration="0" />
                          <DoubleAnimation Storyboard.TargetName="SwitchKnobOff" Storyboard.TargetProperty="Opacity" To="0" Duration="0" />
                        </Storyboard>
                      </VisualState>
                    </VisualStateGroup>
                    <VisualStateGroup x:Name="ContentStates">
                      <VisualState x:Name="OffContent"><VisualState.Setters><Setter Target="OffContentPresenter.Opacity" Value="1" /></VisualState.Setters></VisualState>
                      <VisualState x:Name="OnContent"><VisualState.Setters><Setter Target="OnContentPresenter.Opacity" Value="1" /></VisualState.Setters></VisualState>
                    </VisualStateGroup>
                  </VisualStateManager.VisualStateGroups>
                  <Grid.RowDefinitions><RowDefinition Height="Auto" /><RowDefinition Height="Auto" /></Grid.RowDefinitions>
                  <ContentPresenter x:Name="HeaderContentPresenter" Content="{TemplateBinding Header}"
                         ContentTemplate="{TemplateBinding HeaderTemplate}" Foreground="{TemplateBinding Foreground}"
                         Margin="0,0,0,8" TextWrapping="Wrap" Visibility="Collapsed"
                         IsHitTestVisible="False" AutomationProperties.AccessibilityView="Raw" />
                  <Grid x:Name="SwitchLayout" Grid.Row="1" MinHeight="44" HorizontalAlignment="Left">
                    <Grid.ColumnDefinitions><ColumnDefinition Width="52" /><ColumnDefinition Width="0" /><ColumnDefinition Width="Auto" /></Grid.ColumnDefinitions>
                    <Grid x:Name="SwitchAreaGrid" Grid.ColumnSpan="3" Background="Transparent" CornerRadius="16" Control.IsTemplateFocusTarget="True" />
                    <ContentPresenter x:Name="OffContentPresenter" Grid.Column="2" Opacity="0" Content="{TemplateBinding OffContent}"
                        ContentTemplate="{TemplateBinding OffContentTemplate}" Foreground="{TemplateBinding Foreground}"
                        VerticalAlignment="Center" IsHitTestVisible="False" AutomationProperties.AccessibilityView="Raw" />
                    <ContentPresenter x:Name="OnContentPresenter" Grid.Column="2" Opacity="0" Content="{TemplateBinding OnContent}"
                        ContentTemplate="{TemplateBinding OnContentTemplate}" Foreground="{TemplateBinding Foreground}"
                        VerticalAlignment="Center" IsHitTestVisible="False" AutomationProperties.AccessibilityView="Raw" />
                    <Rectangle x:Name="OuterBorder" Width="52" Height="32" RadiusX="16" RadiusY="16" StrokeThickness="{ThemeResource PyDeckSwitchBorderThickness}"
                        Fill="{ThemeResource ToggleSwitchFillOff}" Stroke="{ThemeResource ToggleSwitchStrokeOff}" />
                    <Rectangle x:Name="SwitchKnobBounds" Width="52" Height="32" RadiusX="16" RadiusY="16" StrokeThickness="{ThemeResource PyDeckSwitchBorderThickness}" Opacity="0"
                        Fill="{ThemeResource ToggleSwitchFillOn}" Stroke="{ThemeResource ToggleSwitchStrokeOn}" />
                    <Grid x:Name="SwitchKnob" Width="32" Height="32" HorizontalAlignment="Left">
                      <Ellipse x:Name="StateLayer" Width="40" Height="40" Margin="-4" Fill="{ThemeResource PyDeckSwitchStateLayer}" Opacity="0" />
                      <Ellipse x:Name="SwitchKnobOff" Width="16" Height="16" Fill="{ThemeResource ToggleSwitchKnobFillOff}" />
                      <Ellipse x:Name="SwitchKnobOn" Width="24" Height="24" Fill="{ThemeResource ToggleSwitchKnobFillOn}" Opacity="0" />
                      <Grid.RenderTransform><TranslateTransform x:Name="KnobTranslateTransform" /></Grid.RenderTransform>
                    </Grid>
                    <Thumb x:Name="SwitchThumb" Grid.ColumnSpan="3" AutomationProperties.AccessibilityView="Raw">
                      <Thumb.Template><ControlTemplate TargetType="Thumb"><Rectangle Fill="Transparent" /></ControlTemplate></Thumb.Template>
                    </Thumb>
                  </Grid>
                </Grid>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """;
}
