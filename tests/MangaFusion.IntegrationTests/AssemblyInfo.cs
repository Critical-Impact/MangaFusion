// These tests boot the real host via WebApplicationFactory. Hangfire keeps global static state
// (JobStorage.Current / GlobalConfiguration), so running multiple hosts in parallel lets one test's
// storage stomp another's. Run the integration tests serially to keep them deterministic.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
