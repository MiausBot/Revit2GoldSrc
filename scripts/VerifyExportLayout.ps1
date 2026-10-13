param([string]$OutputDirectory = 'artifacts/layout-preview', [switch]$Source)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml
$layoutSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../Revit2GoldSrc.Revit/ExportWindowLayout.cs') -Raw
Add-Type -TypeDefinition ($layoutSource.Replace('internal static class', 'public static class')) -ReferencedAssemblies PresentationFramework, PresentationCore, WindowsBase, System.Xaml
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
foreach ($percent in @(100,150,200)) {
    foreach ($physical in @(@(1280,1024), @(1120,720))) {
        $factor = $percent / 100.0; $width = $physical[0] / $factor; $height = $physical[1] / $factor
        $form = New-Object Windows.Controls.StackPanel
        foreach ($number in 1..60) {
            $label = New-Object Windows.Controls.TextBlock
            $label.Text = "Opcion de exportacion $number"; $label.Margin = '0,8,0,3'; $form.Children.Add($label) | Out-Null
            $field = New-Object Windows.Controls.TextBox; $field.Text = 'Materiales de Revit / configuracion'; $form.Children.Add($field) | Out-Null
        }
        $progress = New-Object Windows.Controls.ProgressBar; $progress.Height = 5
        $status = New-Object Windows.Controls.TextBlock; $status.Text = 'Analisis terminado: 1184 prismas comprobados. No se ha escrito ningun archivo.'; $status.TextWrapping = 'Wrap'
        $log = New-Object Windows.Controls.TextBox
        $log.IsReadOnly = $true; $log.AcceptsReturn = $true; $log.TextWrapping = 'Wrap'; $log.VerticalScrollBarVisibility = 'Auto'
        $log.Text = (1..1000 | ForEach-Object { "Elemento $_ : Solido no verificable. Aproximacion supera calidad; error de volumen y desviacion superficial." }) -join "`n"
        $buttons = New-Object Windows.Controls.WrapPanel; $buttons.HorizontalAlignment = 'Right'; $buttons.Margin = '0,12,0,0'
        $actionNames = if ($Source) { @('Analizar','Exportar VMF','Compilar BSP','Escena de prueba','Guía Nav Mesh','Cancelar / Cerrar') } else { @('Analizar modelo','Exportar MAP','Compilar MAP a BSP','Registrar WAD en J.A.C.K.','Cancelar') }
        foreach ($text in $actionNames) {
            $button = New-Object Windows.Controls.Button; $button.Content = $text; $button.Padding = '12,6'; $button.Margin = '6,0,0,0'; $buttons.Children.Add($button) | Out-Null
        }
        $root = [Revit2GoldSrc.Revit.ExportWindowLayout]::Create($form,$progress,$status,$log,$buttons)
        $hostPanel = New-Object Windows.Controls.Border; $hostPanel.Background = [Windows.Media.Brushes]::White; $hostPanel.Child = $root
        $hostPanel.Measure([Windows.Size]::new($width,$height)); $hostPanel.Arrange([Windows.Rect]::new(0,0,$width,$height)); $hostPanel.UpdateLayout()
        foreach ($button in $buttons.Children) {
            $position = $button.TranslatePoint([Windows.Point]::new(0,0),$root)
            if ($position.Y -lt 0 -or $position.Y + $button.ActualHeight -gt $root.ActualHeight + 0.01) { throw "Boton fuera de vista: $percent %, $width x $height" }
        }
        if ($root.Children[0].ActualHeight -lt 50 -or $log.ActualHeight -lt 50 -or $log.ExtentHeight -le $log.ViewportHeight) { throw 'Las opciones o el reporte no tienen un area de desplazamiento util.' }
        $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap($physical[0],$physical[1],(96*$factor),(96*$factor),[Windows.Media.PixelFormats]::Pbgra32)
        $bitmap.Render($hostPanel); $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder; $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
        $path = Join-Path $OutputDirectory "layout-$($physical[0])x$($physical[1])-$percent.png"
        $stream = [IO.File]::Create([IO.Path]::GetFullPath($path)); try { $encoder.Save($stream) } finally { $stream.Dispose() }
        Write-Output "Correcto: $percent %, $width x $height DIP; 1000 diagnosticos, opciones y $($actionNames.Count) botones accesibles."
    }
}


