"""One-sided cooler surface coverage checks using exported triangle winding and ray hits."""
import math
from collections import Counter


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sub(first, second):
    return tuple(a - b for a, b in zip(first, second))


def dot(first, second):
    return sum(a * b for a, b in zip(first, second))


def cross(first, second):
    return (first[1] * second[2] - first[2] * second[1], first[2] * second[0] - first[0] * second[2],
            first[0] * second[1] - first[1] * second[0])


def first_triangle_hit(geometry, part, origin, direction):
    nearest = None
    for name, mesh in geometry.items():
        if not name.startswith("Unscience_Cooler_" + part):
            continue
        positions, normals, indices = mesh["positions"], mesh["normals"], mesh["indices"]
        for offset in range(0, len(indices), 3):
            a, b, c = indices[offset:offset + 3]
            edge1, edge2 = sub(positions[b], positions[a]), sub(positions[c], positions[a])
            perpendicular = cross(direction, edge2)
            determinant = dot(edge1, perpendicular)
            if abs(determinant) < 1e-12:
                continue
            relative = sub(origin, positions[a])
            u = dot(relative, perpendicular) / determinant
            plane = cross(relative, edge1)
            v = dot(direction, plane) / determinant
            distance = dot(edge2, plane) / determinant
            if u < -1e-7 or v < -1e-7 or u + v > 1 + 1e-7 or distance <= 1e-7:
                continue
            if nearest is None or distance < nearest["distance"] - 1e-7:
                face = cross(edge1, edge2)
                length = math.sqrt(dot(face, face))
                face = tuple(value / length for value in face)
                nearest = {"mesh": name, "distance": distance, "normal": face,
                           "normals_agree": all(dot(face, normals[index]) > 0.5 for index in (a, b, c)),
                           "point": tuple(origin[axis] + direction[axis] * distance for axis in range(3))}
    return nearest


def check_surfaces(geometry):
    result = {}

    def check(label, part, origin, direction, z=None, z_range=None, material_mesh=None):
        hit = first_triangle_hit(geometry, part, origin, direction)
        require(hit is not None, f"{label}: visible geometry has a coverage hole")
        require(dot(hit["normal"], direction) < -1e-6, f"{label}: nearest {hit['mesh']} triangle has backward winding")
        require(hit["normals_agree"], f"{label}: exported normals disagree with geometric winding")
        if z is not None:
            require(abs(hit["point"][2] - z) <= 1e-5, f"{label}: hit wrong surface height {hit['point'][2]}")
        if z_range is not None:
            require(z_range[0] <= hit["point"][2] <= z_range[1], f"{label}: missing underside before the lid roof")
        if material_mesh is not None:
            require(hit["mesh"] == material_mesh, f"{label}: expected native foil mesh, got {hit['mesh']}")
        result[label] = hit
        return hit

    # Native KSA -Z is up. Floor rays start inside the open cavity and look down.
    for x in (-0.12, 0.0, 0.12):
        for y in (-0.20, 0.0, 0.20):
            check(f"body floor {x},{y}", "Body", (x, y, -0.30), (0, 0, 1), z=-0.112,
                  material_mesh="Unscience_Cooler_BodyFoil")
    check("exterior body bottom", "Body", (0, 0, 0.1), (0, 0, -1), z=0.0)

    # Lid vertices are hinge-local: center X=.19, underside Z=-.014. Inspect the center,
    # side bands, hinge/front and diagonals; a distant roof is not acceptable coverage.
    samples = [(0.19, 0), (0.19, -0.32), (0.19, 0.32), (0.38, 0), (-0.005, 0),
               (0.365, -0.29), (0.365, 0.29)]
    for x, y in samples:
        check(f"lid underside {x},{y}", "Lid", (x, y, 0.10), (0, 0, -1),
              z=-0.014 if (x, y) == (0.19, 0) else None, z_range=(-0.015, 0.003),
              material_mesh="Unscience_Cooler_LidFoil" if (x, y) == (0.19, 0) else None)

    # Four inward-facing tub walls at mid-height must present foil, not the outer shell.
    for direction in ((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0)):
        hit = check(f"inner wall {direction}", "Body", (0, 0, -0.25), direction,
                    material_mesh="Unscience_Cooler_BodyFoil")
        minimum, maximum = (0.14, 0.18) if direction[0] else (0.25, 0.30)
        require(minimum <= hit["distance"] <= maximum, "Inner wall ray missed the expected tub profile")
    result["lid_seam"] = check_lid_seam(geometry)
    return result


def check_lid_seam(geometry):
    edges, directions = Counter(), Counter()
    for name in ("Unscience_Cooler_LidShell", "Unscience_Cooler_LidFoil"):
        mesh = geometry[name]
        # Material/UV splits duplicate vertices. Weld geometric positions at one-micrometre precision.
        points = [tuple(round(value, 6) for value in position) for position in mesh["positions"]]
        indices = mesh["indices"]
        for offset in range(0, len(indices), 3):
            a, b, c = (points[index] for index in indices[offset:offset + 3])
            for first, second in ((a, b), (b, c), (c, a)):
                require(first != second, "Lid seam contains a collapsed edge")
                edge = tuple(sorted((first, second)))
                edges[edge] += 1
                directions[edge] += 1 if first < second else -1
    require(all(count == 2 for count in edges.values()), "Lid shell/foil has an open or non-manifold seam")
    require(all(direction == 0 for direction in directions.values()), "Lid shell/foil seam has inconsistent winding")
    return {"welded_edges": len(edges), "closed": True}
