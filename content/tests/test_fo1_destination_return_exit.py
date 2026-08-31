from __future__ import annotations
import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
FO1=ROOT/'runtime'/'src'/'Campaigns'/'Fallout1'
class Fo1DestinationReturnExitTest(unittest.TestCase):
 def test_compiler_derives_reciprocal_values_from_the_forward_transition(self):
  tool=(ROOT/'content'/'tools'/'prepare_fo1_destination_exit_grid_return.py').read_text(encoding='utf-8')
  self.assertIn('reciprocalTriggers',tool);self.assertIn('instanceValues',tool);self.assertIn('forward transition has no unique reciprocal exit-grid values',tool);self.assertIn('existing-bound-source-scene-only',tool);self.assertIn('mapsHeader',tool);self.assertIn('refusing to overwrite destination return exit-grid descriptor',tool)
 def test_runtime_restores_only_the_hash_bound_v13ent_map_after_committed_return(self):
  session=(FO1/'Fo1TacticalSession.cs').read_text(encoding='utf-8');flow=(FO1/'Fo1NewGameFlow.cs').read_text(encoding='utf-8');coord=(ROOT/'runtime'/'src'/'RuntimeCoordinator.cs').read_text(encoding='utf-8')
  self.assertIn('TryActivateDestinationReturnExitGrid',session);self.assertIn('EnterCommittedSourceReturn',session);self.assertIn('RestoreSourceTacticalState(reverse.DestinationTile)',session);self.assertIn('_sourceMapSha256',session);self.assertIn('reverse.DestinationMapSha256, _sourceMapSha256',session);self.assertIn('reverse.DestinationElevation',session);self.assertIn('reverse.DestinationRotation',session);self.assertIn('SaveReturnedSourceMap',session);self.assertIn('LoadSavedSourceReturn',session);self.assertIn('duplicate MAP inventory host outside the explicit returned-map boundary',session);self.assertIn('_loadedDestinationPresentation is null &&\n            !_returnedToSource',session);self.assertIn('source-return',session);self.assertNotIn('17690',session);self.assertIn('opennv-fo1-v13ent-reciprocal-return-proof/v1',flow);self.assertIn('RunDestinationReturnExitColdRestoreProof',flow);self.assertIn('fo1-destination-return-exit-proof',coord)
if __name__=='__main__':unittest.main()
