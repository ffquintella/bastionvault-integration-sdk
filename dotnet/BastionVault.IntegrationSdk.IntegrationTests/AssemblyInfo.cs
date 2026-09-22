// ITG-012: scenarios run in parallel against the one server. xUnit parallelises between
// collections and each test class is its own collection by default, so the default behaviour is
// already the required one; SerialGate, not a collection attribute, is what makes `serial`
// scenarios exclusive (see Harness/SerialGate.cs for the rejected alternative).
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerClass, MaxParallelThreads = 4)]

// Suite teardown is driven from AppDomain.ProcessExit (Harness/IntegrationHarness.cs) rather than
// from a custom ITestFramework: in xUnit 2.9.3 neither TestFramework.Dispose nor
// TestFrameworkExecutor<T>.Dispose is virtual, so there is no supported end-of-assembly hook to
// override. The consequence is recorded rather than worked around - the run summary always
// reaches integration-run.log in the test output directory, and reaches the runner console only
// when the runner is still draining stdout at process exit.
