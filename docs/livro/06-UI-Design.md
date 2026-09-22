# Capítulo 6: UI Premium - Dark Mode Empresarial

## 6.1 Paleta de Cores

Defina em `Themes/DarkTheme.xaml`:

```xml
<Color x:Key="PrimaryDarkColor">#0A1628</Color> <!-- fundo principal -->
<Color x:Key="SecondaryDarkColor">#111F3A</Color> <!-- sidebar, header -->
<Color x:Key="CardDarkColor">#162447</Color> <!-- cards -->
<Color x:Key="SurfaceDarkColor">#1B2E5C</Color> <!-- inputs, surface -->
<Color x:Key="BorderDarkColor">#243A6B</Color> <!-- bordas discretas -->
<Color x:Key="ElectricBlueColor">#2D7FF9</Color> <!-- primária -->
<Color x:Key="AccentGreenColor">#00D26A</Color> <!-- sucesso -->

<SolidColorBrush x:Key="PrimaryDarkBrush" Color="{StaticResource PrimaryDarkColor}"/>
<!-- ... -->

<LinearGradientBrush x:Key="PrimaryGradientBrush" StartPoint="0,0" EndPoint="1,1">
    <GradientStop Color="#2D7FF9" Offset="0"/>
    <GradientStop Color="#1A5FCC" Offset="1"/>
</LinearGradientBrush>

<DropShadowEffect x:Key="CardShadow" BlurRadius="20" ShadowDepth="0" Opacity="0.3" Color="#000000"/>
<CornerRadius x:Key="LargeCorner">14</CornerRadius>
```

## 6.2 Controles Premium

### PrimaryButton
```xml
<Style x:Key="PrimaryButton" TargetType="Button">
    <Setter Property="Background" Value="{StaticResource PrimaryGradientBrush}"/>
    <Setter Property="Foreground" Value="White"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="Button">
                <Border Background="{TemplateBinding Background}" CornerRadius="10" Effect="{StaticResource ButtonShadow}" Padding="{TemplateBinding Padding}">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter Property="Background" Value="{StaticResource ElectricBlueLightBrush}"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

Crie também: SuccessButton (green gradient), SecondaryButton (surface + border), IconButton (36x36 transparent), CardBorder, SidebarButton (RadioButton com Tag icon), SearchTextBox, ModernProgressBar (6px height), ModernToggle (44x24), ScrollBar (6px).

### SidebarButton
```xml
<Style x:Key="SidebarButton" TargetType="RadioButton">
    <Setter Property="Height" Value="42"/>
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="RadioButton">
                <Border x:Name="Bd" Background="Transparent" CornerRadius="10" Padding="16,0">
                    <Grid>
                        <Grid.ColumnDefinitions><ColumnDefinition Width="22"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                        <TextBlock x:Name="Icon" Text="{TemplateBinding Tag}" FontSize="14" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        <ContentPresenter Grid.Column="1" VerticalAlignment="Center" Margin="12,0,0,0"/>
                    </Grid>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsChecked" Value="True">
                        <Setter TargetName="Bd" Property="Background" Value="{StaticResource ElectricBlueBrush}"/>
                        <Setter Property="Foreground" Value="White"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

## 6.3 MainWindow - Header + Sidebar + Content

Estrutura:
```xml
<Window WindowStyle="None">
    <Grid>
        <Grid.RowDefinitions><RowDefinition Height="64"/><RowDefinition Height="*"/></Grid.RowDefinitions>
        <!-- HEADER -->
        <Border Grid.Row="0" Background="{StaticResource SecondaryDarkBrush}">
            <Grid>
                <Grid.ColumnDefinitions><ColumnDefinition Width="280"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                <!-- Logo HL + Title -->
                <!-- Search Box -->
                <!-- Language Combo + Notifications + Settings + Min/Max/Close -->
            </Grid>
        </Border>
        <!-- MAIN -->
        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions><ColumnDefinition Width="280"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
            <!-- SIDEBAR -->
            <Border Grid.Column="0" Background="{StaticResource SecondaryDarkBrush}">
                <ScrollViewer>
                    <StackPanel>
                        <TextBlock Text="PRINCIPAL" Style="SectionHeader"/>
                        <RadioButton Style="{StaticResource SidebarButton}" Content="Painel Principal" Tag="🏠" Command="{Binding NavigateCommand}" CommandParameter="Dashboard"/>
                        <!-- ... 12 itens -->
                    </StackPanel>
                </ScrollViewer>
            </Border>
            <!-- CONTENT -->
            <ContentControl Grid.Column="1" Content="{Binding CurrentViewModel}">
                <ContentControl.Resources>
                    <DataTemplate DataType="{x:Type vm:DashboardViewModel}"><views:DashboardView/></DataTemplate>
                </ContentControl.Resources>
            </ContentControl>
        </Grid>
    </Grid>
</Window>
```

Code-behind para drag:
```csharp
MouseDown += (s,e) => { if (e.GetPosition(this).Y < 64) DragMove(); };
```

## 6.4 Dashboard - O Cartão de Visita

Layout:
- Top row: Health Card (circular progress) + Metrics (CPU/RAM/GPU/Disco) + System Info mini
- Quick Actions: 6 atalhos em UniformGrid
- Bottom: Categories + Last Actions

Health Card com Ellipse e StrokeDashArray para progresso circular.

Metrics com 4 cards SurfaceDark, cada com ícone colorido, valor grande e ProgressBar.

## 6.5 Outros Views

Cada view segue mesmo padrão:
- Título grande branco Bold 22px
- Subtítulo secondary 12px
- CardBorder com seções
- Uso de SurfaceDark para itens internos

Exemplo BoosterView: 3 cards para modos Econômico/Equilibrado/Desempenho, cada com borda destacada quando SelectedMode ==.

TweaksView: WrapPanel de cards 320px width, cada com estado atual/recomendado, risco, botões Aplicar/Reverter.

CleanupView: Lista com CheckBox, nome, path, tamanho, categoria, total no header, botão Limpar.

GamesView: Lista jogos + emuladores esquerda, detalhes direita com booster settings CheckBoxes.

## 6.6 Converters

Crie `Converters.xaml` com:
- CountToVisibility
- InverseBoolToVisibility
- BoolToVisibility
- PercentageToBrush (verde >=80, amarelo >=50, vermelho <50)
- StringToVisibility (Personalizado)
- InverseBool
- PercentageToWidth
- BoolToColor

## 6.7 Tipografia e Espaçamento

- Fonte: Segoe UI (padrão Windows)
- Títulos: 22px Bold
- Section headers: 10px Bold LetterSpacing 1, Tertiary color
- Body: 11-12px
- Padding cards: 20px
- Margin entre cards: 12-16px
- CornerRadius: 10-14px

Evite:
- Excesso gradientes (use só em primary buttons)
- Excesso efeitos
- Textos exagerados
- Emojis demais (use com moderação para ícones)

Próximo: implementando módulos específicos.
