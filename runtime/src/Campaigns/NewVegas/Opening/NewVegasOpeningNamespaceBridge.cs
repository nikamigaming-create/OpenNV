// The campaign opening consumes shared runtime composition types, while shared
// runtime state and routing refer back to the opening. Keep that compile-time
// join here so hierarchy-only moves do not alter behavior or call sites.
global using OpenNV.Runtime;
global using OpenNV.Runtime.Campaigns.NewVegas.Opening;
