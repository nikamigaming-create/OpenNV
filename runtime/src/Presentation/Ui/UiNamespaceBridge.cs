// UI presentation consumes shared runtime state, while gameplay owns the
// snapshots it presents. Keep that compile-time join here so both stay intact.
global using OpenNV.Runtime;
global using OpenNV.Runtime.Presentation.Ui;
