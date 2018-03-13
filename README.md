# Visual Studio SDK Test Framework

The VS SDK Test Framework is a library for your unit tests that exercise VS code to use
so that certain core VS functionality works outside the VS process so your unit tests can function.
For example, `ThreadHelper` and obtaining global services from the static `ServiceProvider`
tend to fail in unit tests without this library installed.

## Referencing

### For tests that build outside the VS repo:

To reference this test framework outside the VS repo,
you will need to add this as a package source in your [nuget.config file](https://docs.microsoft.com/en-us/nuget/schema/nuget-config-file#packagesources):

    https://devdiv.pkgs.visualstudio.com/_packaging/VSIDE-RealSigned-release/nuget/v3/index.json

Then install its NuGet package:

    Install-Package Microsoft.VisualStudio.Sdk.TestFramework -Pre

Make sure your unit test project generates the required binding redirects by adding these two properties to your project file:

```xml
<AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>
<GenerateBindingRedirectsOutputType>true</GenerateBindingRedirectsOutputType>
```

### For tests that build within the VS repo:

Add these references to your project:

```xml
<Reference Include="$(PkgMicrosoft_VisualStudio_Sdk_TestFramework)\lib\net46\Microsoft.VisualStudio.Sdk.TestFramework.dll" />
<Reference Include="$(PkgSystem_Collections_Immutable)\lib\netstandard1.0\System.Collections.Immutable.dll" />
<Reference Include="$(PkgVS_ExternalAPIs_Moq)\v4.2\Moq.dll" />
```    

Add the following binding redirects to your test project, via an app.config.tt file as shown below.
If you do not already have an app.config.tt file (or perhaps it is called just app.config),
you can create your T4 macro-enabled file as demonstrated by [this pull request](https://devdiv.visualstudio.com/DevDiv/Connected%20Experience/_git/VS/pullrequest/62848?_a=files&path=%2Fsrc%2Fdebugger%2FRazor%2FUnitTests).

You may need to adjust the version values in the `Moq` binding redirect to match the available version.

```xml
<?xml version="1.0" ?>
<#@template hostspecific="true"#>
<#@ include file="BrandNames.tt" #>
<#@ include file="..\..\..\ProductData\AssemblyVersions.tt" #>
<configuration>
  <runtime>
    <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
      <dependentAssembly>
        <assemblyIdentity name="Microsoft.VisualStudio.Composition.Configuration" publicKeyToken="b03f5f7f11d50a3a" culture="neutral"/>
        <bindingRedirect oldVersion="0.0.0.0-<#= MicrosoftVisualStudioCompositionVersion #>" newVersion="<#= MicrosoftVisualStudioCompositionVersion #>"/>
      </dependentAssembly>
      <dependentAssembly>
        <assemblyIdentity name="Microsoft.VisualStudio.Composition" publicKeyToken="b03f5f7f11d50a3a" culture="neutral"/>
        <bindingRedirect oldVersion="0.0.0.0-<#= MicrosoftVisualStudioCompositionVersion #>" newVersion="<#= MicrosoftVisualStudioCompositionVersion #>"/>
      </dependentAssembly>
      <dependentAssembly>
        <assemblyIdentity name="Microsoft.VisualStudio.Threading" publicKeyToken="b03f5f7f11d50a3a" culture="neutral"/>
        <bindingRedirect oldVersion="0.0.0.0-<#= MicrosoftVisualStudioThreadingVersion #>" newVersion="<#= MicrosoftVisualStudioThreadingVersion #>"/>
      </dependentAssembly>
      <dependentAssembly>
        <assemblyIdentity name="Microsoft.VisualStudio.Validation" publicKeyToken="b03f5f7f11d50a3a" culture="neutral"/>
        <bindingRedirect oldVersion="0.0.0.0-<#= MicrosoftVisualStudioValidationVersion #>" newVersion="<#= MicrosoftVisualStudioValidationVersion #>"/>
      </dependentAssembly>
      <dependentAssembly>
        <assemblyIdentity name="System.Collections.Immutable" publicKeyToken="b03f5f7f11d50a3a" culture="neutral" />
        <bindingRedirect oldVersion="0.0.0.0-<#= SystemCollectionsImmutableVersion #>"  newVersion="<#= SystemCollectionsImmutableVersion #>"/>
      </dependentAssembly>
      <dependentAssembly>
        <assemblyIdentity name="Moq" publicKeyToken="69f491c39445e920" culture="neutral" />
        <bindingRedirect oldVersion="0.0.0.0-4.2.9999.999" newVersion="4.2.1502.911" />
      </dependentAssembly>
    </assemblyBinding>
  </runtime>
</configuration>
```

## Unit test source code changes

### Xunit instructions

Add this class to your project (if a MockedVS.cs file was not added to your project automatically):

```csharp
using Xunit;

/// <summary>
/// Defines the "MockedVS" xunit test collection.
/// </summary>
[CollectionDefinition(Collection)]
public class MockedVS : ICollectionFixture<GlobalServiceProvider>, ICollectionFixture<MefHosting>
{
    /// <summary>
    /// The name of the xunit test collection.
    /// </summary>
    public const string Collection = "MockedVS";
}
```

Then for *each* of your test classes, apply the `Collection` attribute and
add a parameter and statement to your constructor:

```csharp
[Collection(MockedVS.Collection)]
public class YourTestClass
{
    public TestFrameworkTests(GlobalServiceProvider sp)
    {
        sp.Reset();
    }
}
```

### MSTest instructions

Add these members to *one* of your test classes:

```csharp
[TestClass]
public class SharedClass
{
    internal static GlobalServiceProvider MockServiceProvider { get; private set; }

    [AssemblyInitialize]
    public static void AssemblyInit(TestContext context)
    {
        MockServiceProvider = new GlobalServiceProvider();
    }
}
```

Then in *each* of your test classes, Reset() the static service provider created earlier:

```csharp
[TestInitialize]
public void TestInit()
{
    SharedClass.MockServiceProvider.Reset();
}
```

For a sample of applying this pattern to an MSTest project within the VS repo, check out [this pull request](https://devdiv.visualstudio.com/DevDiv/Connected%20Experience/_git/VS/pullrequest/57056?_a=files&path=%2Fsrc%2Fenv%2Fshell%2FConnected%2Ftests).

## Main Thread considerations

This library will create a mocked up UI thread, such that `ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync()`
can switch to it. Your unit tests do *not* start on this mocked up UI thread. If your product code contains checks
that it is invoked on the UI thread (e.g. `ThreadHelper.ThrowIfNotOnUIThread()`) your test method should look like this:

```csharp
[TestMethod] // or [Fact]
public async Task VerifyWeDoSomethingGood()
{
    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
    MyVSPackage.DoSomethingAwesome();
}
```

## Built in service mocks

There are a collection of "base services" that the VSSDKTestFx comes with mocks for.
Calling `GlobalServiceProvider.AddService` for any of these will result in an `InvalidOperationException` being thrown claiming
that the service is already added.

These services include:

* `SVsActivityLog`
* `OLE.Interop.IServiceProvider`
* `SVsTaskSchedulerService`
* `SVsUIThreadInvokerPrivate`

More may be added and can be found in [source code](https://devdiv.visualstudio.com/DefaultCollection/DevDiv/Connected%20Experience/_git/VSSDKTestFx?_a=contents&path=%2Fsrc%2FMicrosoft.VisualStudio.Sdk.TestFramework%2FGlobalServiceProvider.cs&version=GBmaster&line=194&lineStyle=plain&lineEnd=198&lineStartColumn=1&lineEndColumn=1).
