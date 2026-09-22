// Samples that configure the client from BASTIONVAULT_* environment variables mutate
// process-global state for the duration of the test. Process state is not collection-scoped, so
// the whole assembly runs serially. It is a small assembly; the cost is seconds.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
