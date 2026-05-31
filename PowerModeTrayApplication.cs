<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.19041.0</TargetPlatformMinVersion>
    <RootNamespace>Mica.PowerModeTray.WinUI</RootNamespace>
    <AssemblyName>PowerModeTray</AssemblyName>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <UseWinUI>true</UseWinUI>
    <UseWindowsForms>true</UseWindowsForms>
    <ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>
    <WindowsPackageType>None</WindowsPackageType>
    <Nullable>enable</Nullable>
    <ApplicationIcon>..\native\assets\PowerModeTray.ico</ApplicationIcon>
    <Platforms>x64</Platforms>
    <RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
    <!-- Public-release style deployment: small framework-dependent package with dependency bootstrap. -->
    <SelfContained>false</SelfContained>
    <PublishSelfContained>false</PublishSelfContained>
    <WindowsAppSDKSelfContained>false</WindowsAppSDKSelfContained>
    <StartupObject>Mica.PowerModeTray.WinUI.Program</StartupObject>
  
    <Product>Power Mode</Product>
    <AssemblyTitle>Power Mode</AssemblyTitle>
    <FileDescription>Power Mode</FileDescription>
    <Company>KomCom</Company>
    <Authors>KomCom</Authors>
    <Version>2.7.15</Version>
    <AssemblyVersion>2.7.15.0</AssemblyVersion>
    <FileVersion>2.7.15.0</FileVersion>
    <InformationalVersion>2.7.15</InformationalVersion>
  </PropertyGroup>

  <!--
    This tray app builds its WinUI surface from C# code.
    Keep App.xaml in the source tree as documentation only, but exclude XAML
    from the SDK build so Microsoft.WinFX/WPF targets don't try to compile it.
  -->
  <ItemGroup>
    <ApplicationDefinition Remove="**\*.xaml" />
    <Page Remove="**\*.xaml" />
    <None Include="App.xaml" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.6.250205002" />
    <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.4654" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="..\native\assets\PowerModeTray.ico">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
</Project>
