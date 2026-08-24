import unittest
from pathlib import Path
import sys


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from actor_review_coverage import (  # noqa: E402
    appearance_counts,
    appearance_coverage_rows,
    placement_coverage_rows,
)


class ActorReviewCoverageTest(unittest.TestCase):
    def test_missing_and_failed_rows_remain_visible(self):
        sources = [
            {
                "reviewKey": "FalloutNV.esm:000001@first",
                "baseFormKey": "FalloutNV.esm:000001",
                "baseRuntimeFormId": "00000001",
                "recordType": "CREA",
                "editorId": "Creature",
                "requiredShots": ["front-detail"],
            },
            {
                "reviewKey": "FalloutNV.esm:000002@second",
                "baseFormKey": "FalloutNV.esm:000002",
                "baseRuntimeFormId": "00000002",
                "recordType": "NPC_",
                "editorId": "Person",
                "requiredShots": ["front-detail"],
            },
        ]
        reports = {
            sources[0]["reviewKey"]: {
                "recordType": "CREA",
                "comparisonCount": 1,
                "_path": "C:/evidence/report.json",
                "_sha256": "a" * 64,
                "coverageLedgerRow": {
                    "retailEvidenceStatus": "pass",
                    "godotCaptureStatus": "pass",
                    "matchedComparisonStatus": "fail",
                    "humanReviewStatus": "pending",
                    "lookedAt": False,
                    "parityStatus": "fail",
                },
            }
        }
        rows = appearance_coverage_rows(sources, reports)
        counts = appearance_counts(rows)
        self.assertEqual(counts["total"], 2)
        self.assertEqual(counts["evidenceReports"], 1)
        self.assertEqual(counts["missingEvidence"], 1)
        self.assertEqual(counts["objectiveFailed"], 1)
        self.assertEqual(counts["humanReviewed"], 0)
        self.assertEqual(counts["parityPassed"], 0)

    def test_placement_rows_start_fail_closed(self):
        rows = placement_coverage_rows(
            [
                {
                    "placementFormKey": "FalloutNV.esm:000003",
                    "placementRuntimeFormId": "00000003",
                    "recordType": "ACHR",
                    "cell": {"key": "FalloutNV.esm:000004"},
                    "candidateBaseFormKeys": ["FalloutNV.esm:000002"],
                    "requiredShots": ["in-cell-context", "activity-motion"],
                }
            ]
        )
        self.assertEqual(rows[0]["status"], "missing-evidence")
        self.assertFalse(rows[0]["lookedAt"])
        self.assertEqual(rows[0]["parityStatus"], "fail")


if __name__ == "__main__":
    unittest.main()
