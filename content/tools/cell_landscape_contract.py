"""Pure shared artifact contract for one corpus-bound CELL LAND child."""

from __future__ import annotations

from landscape_catalog import LAND_VERTEX_SIDE
from landscape_stack import OwnedLandscapeSource


LAND_QUAD_SIDE = LAND_VERTEX_SIDE - 1
TRIANGLES_PER_QUAD = 2


def landscape_contract_for(
    source: OwnedLandscapeSource,
    cell: dict[str, object],
    origin: tuple[float, float, float],
) -> dict[str, object]:
    landscape = source.landscape
    return {
        "formKey": source.identity.form_key,
        "cellFormKey": source.identity.cell_form_key,
        "worldspaceFormKey": source.identity.worldspace_form_key,
        "sourcePlugin": source.identity.source_plugin,
        "sourceLocalFormId": source.identity.source_local_form_id,
        "cellCoordinates": list(cell["coordinates"]),
        "originGameUnits": list(origin),
        "flags": landscape.flags,
        "compressionChecksumValid": landscape.compression_checksum_valid,
        "vertices": LAND_VERTEX_SIDE * LAND_VERTEX_SIDE,
        "triangles": LAND_QUAD_SIDE * LAND_QUAD_SIDE * TRIANGLES_PER_QUAD,
        "baseLayers": len(landscape.base_layers),
        "alphaLayers": len(landscape.alpha_layers),
        "textureGraph": source.textures.contracts(),
    }
