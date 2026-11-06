using Xunit;

// UI tests share the Windows desktop, dispatcher resources and global hotkey registrations.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
