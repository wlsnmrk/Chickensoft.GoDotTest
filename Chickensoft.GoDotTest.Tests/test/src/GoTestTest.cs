namespace Chickensoft.GoDotTest.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using Chickensoft.Log;
using Godot;
using GoDotTest;
using LightMock;
using LightMock.Generator;
using LightMoq;
using Shouldly;

public class TestTestAdapter : TestAdapter {
  public Action<Mock<ITestExecutor>> OnExecutorCreated;

  public TestTestAdapter(
    Action<Mock<ITestExecutor>> onExecutorCreated
  ) {
    OnExecutorCreated = onExecutorCreated;
  }

  public override ITestExecutor CreateExecutor(
      ITestMethodExecutor methodExecutor,
      bool stopOnError,
      bool sequential,
      int timeoutMilliseconds
    ) {
    var executor = new Mock<ITestExecutor>();
    OnExecutorCreated?.Invoke(executor);
    return executor.Object;
  }
}

public class GoTestTest : TestClass {
  public GoTestTest(Node testScene) : base(testScene) { }

  [Test]
  public async Task DoesNothingIfNotRunningTests() {
    var adapter = new Mock<TestAdapter>();
    GoTest.Adapter = adapter.Object;
    var testEnv = TestEnvironment.From([]);
    var log = new Mock<ILog>();
    var assembly = Assembly.GetExecutingAssembly();
    await GoTest.RunTests(assembly, TestScene, testEnv, log.Object);
  }

  [Test]
  public async Task ExitsWithFailingExitCodeWhenTestsFail() {
    var testEnv = TestEnvironment.From(
      ["--run-tests=ahem", "--quit-on-finish"]
    );
    var log = new Mock<ILog>();
    var provider = new Mock<ITestProvider>();

    SetupTest(testEnv, log, provider);

    int? testExitCode = null;
    GoTest.OnExit = (node, exitCode) => testExitCode = exitCode;

    await GoTest.RunTests(
      Assembly.GetExecutingAssembly(), TestScene, testEnv, log.Object
    );
    testExitCode.ShouldBe(1);
    provider.VerifyAll();
  }

  [Test]
  public async Task RemovesTraceListenerWhenTestsFail() {
    // will be 1 in VSCode, 2 in VS (it adds its own DefaultTraceListener
    // to run these tests)
    var traceListenerCount = Trace.Listeners.Count;
    var testEnv = TestEnvironment.From(
      [
        "--run-tests=ahem",
        "--listen-trace",
         "--quit-on-finish"
      ]
    );
    var log = new Mock<ILog>();
    var provider = new Mock<ITestProvider>();

    SetupTest(testEnv, log, provider);

    int? testExitCode = null;
    GoTest.OnExit = (node, exitCode) => testExitCode = exitCode;

    await GoTest.RunTests(
      Assembly.GetExecutingAssembly(), TestScene, testEnv, log.Object
    );
    Trace.Listeners.Count.ShouldBe(traceListenerCount);
  }

  [Test]
  public async Task ExitsWithFailingExitCodeWhenTestsFailOnCoverage() {
    var testEnv = TestEnvironment.From(
      ["--run-tests=ahem", "--coverage", "--quit-on-finish"]
    );
    var log = new Mock<ILog>();
    var provider = new Mock<ITestProvider>();

    SetupTest(testEnv, log, provider);

    int? testExitCode = null;
    GoTest.OnForceExit = (node, exitCode) => testExitCode = exitCode;

    await GoTest.RunTests(
      Assembly.GetExecutingAssembly(), TestScene, testEnv, log.Object, null
    );
    testExitCode.ShouldBe(1);
  }

  [Test]
  public async Task TimeoutMillisecondSetterShouldHaveAnImpactWhenCreatingExecutor() {
    var testEnv = TestEnvironment.From(
      ["--run-tests=ahem", "--coverage", "--quit-on-finish"]
    );
    var log = new Mock<ILog>();
    var provider = new Mock<ITestProvider>();
    var timeoutMilliseconds = 123456;

    GoTest.TimeoutMilliseconds = timeoutMilliseconds;
    SetupTest(testEnv, log, provider, timeoutMilliseconds);

    await GoTest.RunTests(
      Assembly.GetExecutingAssembly(), TestScene, testEnv, log.Object, null
    );

    GoTest.TimeoutMilliseconds = 10000;
  }

  private void SetupTest(TestEnvironment testEnv, Mock<ILog> log, Mock<ITestProvider> provider, int expectedTimeout = 10000) {
    provider
          .Setup(provider => provider.GetTestSuitesByPattern(
            The<Assembly>.IsAnyValue, "ahem"
          )).Returns([]);

    var reporter = new Mock<ITestReporter>();
    reporter.Setup(reporter => reporter.HadError).Returns(true);

    var executor = new Mock<ITestExecutor>();
    executor.Setup(
      executor => executor.Run(
        TestScene, The<List<ITestSuite>>.IsAnyValue, reporter.Object
      )
    ).Returns(Task.CompletedTask);

    var adapter = new Mock<ITestAdapter>();
    adapter.Setup(adapter => adapter.CreateTestEnvironment(testEnv))
      .Returns(testEnv);
    adapter.Setup(adapter => adapter.CreateLog(log.Object)).Returns(log.Object);
    adapter.Setup(
      adapter => adapter.CreateProvider()
    ).Returns(provider.Object);
    adapter.Setup(
      adapter => adapter.CreateReporter(log.Object)
    ).Returns(reporter.Object);
    adapter.Setup(adapter => adapter.CreateExecutor(
      The<ITestMethodExecutor>.IsAnyValue,
      The<bool>.IsAnyValue,
      The<bool>.IsAnyValue,
      expectedTimeout
    )).Returns(executor.Object);

    GoTest.Adapter = adapter.Object;
  }

  /// <summary>
  /// Put the default adapter back once we're done testing the test system.
  /// </summary>
  [CleanupAll]
  public void CleanupAll() {
    GoTest.Adapter = GoTest.DefaultAdapter;
    GoTest.OnExit = GoTest.DefaultOnExit;
    GoTest.OnForceExit = GoTest.DefaultOnForceExit;
  }
}
