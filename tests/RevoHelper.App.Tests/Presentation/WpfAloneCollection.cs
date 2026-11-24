namespace RevoHelper.App.Tests.Presentation;

/// <summary>
/// Classes that make their first WPF elements on a thread of their own. Two such classes starting at
/// once could hang the test host for good (InkCenterTests with KeyboardFocusCueTests, about one run in
/// three), so they run alone, after the parallel ones.
/// </summary>
[CollectionDefinition("WPF alone", DisableParallelization = true)]
public sealed class WpfAloneCollection;
