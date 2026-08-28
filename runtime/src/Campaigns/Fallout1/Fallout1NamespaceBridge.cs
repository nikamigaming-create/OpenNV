// The campaign implementation consumes shared runtime composition types, while
// RuntimeCoordinator routes into the campaign. Keep that compile-time join here
// so the coordinator does not need hierarchy-only edits.
global using OpenNV.Runtime;
global using OpenNV.Runtime.Campaigns.Fallout1;
