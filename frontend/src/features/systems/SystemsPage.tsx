import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Chip } from '../../components/Chip';
import { Button } from '../../components/Button';
import { Table, Th, Td } from '../../components/Table';

interface AssemblyItem {
  id: string;
  name: string;
  code: string;
  category: string;
  description: string;
  primaryPlywoodProduct: string;
  primaryPlywoodCode: string;
  fireRating: string;
  acousticRating: string;
  moistureClass: string;
  warranty: string;
  bimStatus: boolean;
  bom: Array<{
    item: string;
    productCode?: string;
    specification: string;
    qty: string;
    role: string;
  }>;
  keyFeatures: string[];
}

const PLYWOOD_SYSTEMS: AssemblyItem[] = [
  {
    id: 'SYS-KIT-01',
    name: 'Heavy-Duty Modular Kitchen Carcass Assembly',
    code: 'SYS-PG-KITCHEN-BWP',
    category: 'Cabinetry & Joinery',
    description:
      'Zero-emission, boiling water proof (BWP) modular kitchen base and overhead cabinetry system. Engineered with Club 700 structural plywood to resist continuous boiling water exposure, aggressive steam, and heavy quartz countertop dead loads.',
    primaryPlywoodProduct: 'Club 700 Structural Plywood',
    primaryPlywoodCode: 'PG-CLUB-700',
    fireRating: 'IS:5509 Class 1 (45 min delay)',
    acousticRating: 'Acoustic Damping Class C',
    moistureClass: 'BWP (100% Boiling Water Proof)',
    warranty: 'Lifetime Warranty (7X Money-Back)',
    bimStatus: true,
    keyFeatures: [
      'Penta-Tech multi-stage calibrated carcass panels for zero surface undulation',
      'Vacuum pressure impregnated anti-termite and anti-borer chemical barrier',
      'E0 Formaldehyde emission standard (< 0.05 ppm) for non-toxic indoor cooking zones',
      'High edge screw-holding capacity (1,350 N) preventing hinge tear-out under heavy pot loads',
    ],
    bom: [
      {
        item: 'Carcass Side & Base Panels',
        productCode: 'PG-CLUB-700',
        specification: '19 mm Club 700 BWP (2440 × 1220 mm), Gurjan core',
        qty: '2.5 Sheets / Base Unit',
        role: 'Primary Structural Load-Bearing Carcass',
      },
      {
        item: 'Cabinet Back Lining',
        productCode: 'PG-CLUB-700',
        specification: '6 mm Club 700 BWP, Calibrated 4-press',
        qty: '1 Sheet / Unit',
        role: 'Rear Moisture Seal & Squareness Stability',
      },
      {
        item: 'Edge Protection',
        specification: '2 mm Polymer PVC/ABS Edge Band with PUR Waterproof Glue',
        qty: '18 m / Unit',
        role: 'Perimeter Moisture & Impact Barrier',
      },
      {
        item: 'Heavy Load Hardware',
        specification: '35 mm Concealed Soft-Close Hinges (Tested 100,000 cycles)',
        qty: '4 Pcs / Shutter',
        role: 'Dynamic Action & Door Suspension',
      },
    ],
  },
  {
    id: 'SYS-PART-02',
    name: 'Fire-Retardant Acoustic Drywall Partition System',
    code: 'SYS-PG-PARTITION-FR60',
    category: 'Architectural Wall Systems',
    description:
      'High-performance internal commercial partition system combining Platinum 2X Fire Retardant Plywood facings with lightweight galvanized steel studs and rockwool cavity insulation. Designed for fire exit corridors and executive boardrooms.',
    primaryPlywoodProduct: 'Platinum Fire Retardant Plywood',
    primaryPlywoodCode: 'PG-PLAT-BWP',
    fireRating: '60 Min Integrity & Insulation (IS:5509 / ASTM E119)',
    acousticRating: 'STC 52 dB / Rw 51 dB',
    moistureClass: 'BWP Marine Grade (Exterior / Wet Corridor)',
    warranty: '30 Years Warranty (2X Money-Back)',
    bimStatus: true,
    keyFeatures: [
      '2X Fire Penetration Delay with nano-engineered intumescent chemical treatment',
      'Low smoke generation index preventing toxic particulate build-up during evacuation',
      'High flexural modulus of rupture (MOR 58 MPa) sustaining lateral impact loads',
      'Readily accepts decorative veneers, acoustic laminates, or direct architectural coatings',
    ],
    bom: [
      {
        item: 'Fire-Retardant Wall Boarding (Both Faces)',
        productCode: 'PG-PLAT-BWP',
        specification: '16 mm Platinum 2X Fire Retardant BWP (2440 × 1220 mm)',
        qty: '2 Sheets / m² wall',
        role: 'Dual-Face Fire Wall & Impact Facing',
      },
      {
        item: 'Galvanized Structural Framework',
        specification: '75 mm Light Gauge Steel C-Studs @ 600 mm c/c (0.55 mm BMT)',
        qty: '3.4 m / m² wall',
        role: 'Vertical Structural Core Skeleton',
      },
      {
        item: 'Acoustic / Thermal Core',
        specification: '50 mm High-Density Resin-Bonded Rockwool (48 kg/m³)',
        qty: '1 m² / m² wall',
        role: 'Cavity Sound Damping & Thermal Dissipation',
      },
      {
        item: 'Intumescent Acoustic Perimeter Mastic',
        specification: 'Silicone Acrylic Fire Sealant (Class A fire stop)',
        qty: '0.6 kg / m² wall',
        role: 'Perimeter Flanking Noise & Smoke Intercept',
      },
    ],
  },
  {
    id: 'SYS-DOOR-03',
    name: 'Engineered Architectural Flush Door Assembly',
    code: 'SYS-PG-FLUSH-DOOR-IS2202',
    category: 'Doors & Entrances',
    description:
      'Engineered solid-core flush door assembly manufactured per BIS IS:2202 Part 1. Features seasoned hardwood timber battens, cross-bands, and calibrated face veneers for complete dimensional stability under shifting atmospheric humidity.',
    primaryPlywoodProduct: '710 Marine Hardwood Core',
    primaryPlywoodCode: 'PG-710-MAR',
    fireRating: 'IS:5509 30-Minute Resistance',
    acousticRating: 'Rw 34 dB Sound Transmission',
    moistureClass: 'BWP / Tropical Weather Tested',
    warranty: '25 Years Warranty',
    bimStatus: true,
    keyFeatures: [
      'Multi-chamber seasoned kilned hardwood core with zero warp or twist tendency',
      'Penta-Press calibrated faces ready for natural teak, oak, or reconstituted veneer layup',
      'Concealed solid hardwood lipping on all four perimeter edges',
      'Borer and fungus resistant with vacuum-pressure copper-chrome-boron preservative',
    ],
    bom: [
      {
        item: 'Solid Core Timber Frame & Battens',
        specification: 'Kiln-Dried Selected Hardwood (Density 650 kg/m³)',
        qty: '1 Shutter Core',
        role: 'Dimensional Stability & Core Mass',
      },
      {
        item: 'Marine Grade Cross-Band Facing',
        productCode: 'PG-710-MAR',
        specification: '4 mm 710 Marine Veneer Facing per IS:710',
        qty: '2 Layers / Shutter',
        role: 'Balanced Cross-Grain Veneer Stabilizer',
      },
      {
        item: 'Architectural Lipping',
        specification: '12 mm Solid Teak / Gurjan Perimeter Hardwood Lippings',
        qty: '5.5 m / Door',
        role: 'Edge Protection & Planing Margin',
      },
      {
        item: 'Drop Acoustic Seal',
        specification: 'Automatic Concealed Mortised Bottom Drop Seal',
        qty: '1 Pc / Door',
        role: 'Air & Sound Transmission Barrier',
      },
    ],
  },
  {
    id: 'SYS-CEIL-04',
    name: 'Acoustic Micro-Perforated Ceiling Panelling Grid',
    code: 'SYS-PG-ACOUSTIC-CEILING',
    category: 'Ceilings & Acoustics',
    description:
      'Concealed T-grid architectural acoustic wood ceiling system. Employs calibrated 12 mm Gold plywood panels with computer-controlled micro-perforations and acoustic tissue backing for concert halls, auditoriums, and open-plan offices.',
    primaryPlywoodProduct: 'Gold Calibrated BWP Plywood',
    primaryPlywoodCode: 'PG-GOLD-BWP',
    fireRating: 'Class 1 Fire Propagation (BS 476 Part 7)',
    acousticRating: 'NRC 0.80 / Sound Absorption Class B',
    moistureClass: 'MR / High Humidity Resistant',
    warranty: '30 Years Warranty + 11-Year Cover',
    bimStatus: true,
    keyFeatures: [
      'CNC precision micro-perforation matrix (diameter 1.5 mm, pitch 4 mm)',
      '11-Year Fully Covered+ restoration guarantee ensuring peace of mind against bowing',
      'Concealed spring-tee suspension grid allowing demountable access to MEP plenum services',
      'Certified low VOC emission for LEED and IGBC Platinum green building projects',
    ],
    bom: [
      {
        item: 'Micro-Perforated Acoustic Panels',
        productCode: 'PG-GOLD-BWP',
        specification: '12 mm Gold BWP Plywood (600 × 600 mm / 1200 × 600 mm)',
        qty: '2.8 Panels / m²',
        role: 'Sound Diffusing & Resonant Absorber Surface',
      },
      {
        item: 'Acoustic Fleece Liner',
        specification: 'Non-Woven Black Acoustic Micro-fiber Tissue (0.2 mm)',
        qty: '1 m² / m²',
        role: 'Viscous Boundary Layer Acoustic Dissipation',
      },
      {
        item: 'Suspension Grid Framework',
        specification: 'Galvanized 24 mm Concealed Spring-Tee Grid with Hanger Rods',
        qty: '3.2 m / m²',
        role: 'Seismic & Gravity Plenum Suspension',
      },
    ],
  },
];

export function SystemsPage() {
  const [selectedId, setSelectedId] = useState<string>('SYS-KIT-01');

  const activeSystem =
    PLYWOOD_SYSTEMS.find((s) => s.id === selectedId) ?? PLYWOOD_SYSTEMS[0];

  return (
    <div style={{ maxWidth: 1280, margin: '0 auto', paddingBottom: 40 }}>
      {/* Page Header */}
      <div className="phead" style={{ marginBottom: 20 }}>
        <div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <h1 style={{ margin: 0 }}>Systems &amp; Assemblies</h1>
            <Chip tone="ok" dot>
              Engineered Wood Architectural Systems
            </Chip>
          </div>
          <p style={{ marginTop: 6, color: 'var(--ink2)', fontSize: 13 }}>
            Modular cabinetry assemblies, fire-rated partition walls, architectural door portals,
            and acoustic ceilings configured using Ply Gallery certified structural panels.
          </p>
        </div>
      </div>

      {/* KPI Headline Cards */}
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
          gap: 14,
          marginBottom: 24,
        }}
      >
        <div className="card pad" style={{ padding: '14px 18px' }}>
          <div className="muted" style={{ fontSize: 11.5, fontWeight: 600, textTransform: 'uppercase' }}>
            Certified Assemblies
          </div>
          <div className="num" style={{ fontSize: 28, fontWeight: 700, margin: '6px 0 2px' }}>
            4 Systems
          </div>
          <div className="muted" style={{ fontSize: 11.5 }}>
            Tested per IS:710, IS:5509 &amp; IS:2202
          </div>
        </div>

        <div className="card pad" style={{ padding: '14px 18px' }}>
          <div className="muted" style={{ fontSize: 11.5, fontWeight: 600, textTransform: 'uppercase' }}>
            Max Fire Resistance
          </div>
          <div className="num" style={{ fontSize: 28, fontWeight: 700, margin: '6px 0 2px', color: 'var(--amb)' }}>
            60 Min
          </div>
          <div className="muted" style={{ fontSize: 11.5 }}>
            Partition system fire integrity rating
          </div>
        </div>

        <div className="card pad" style={{ padding: '14px 18px' }}>
          <div className="muted" style={{ fontSize: 11.5, fontWeight: 600, textTransform: 'uppercase' }}>
            Max Acoustic Damping
          </div>
          <div className="num" style={{ fontSize: 28, fontWeight: 700, margin: '6px 0 2px', color: 'var(--tea)' }}>
            52 dB STC
          </div>
          <div className="muted" style={{ fontSize: 11.5 }}>
            Double-face drywall cavity assembly
          </div>
        </div>

        <div className="card pad" style={{ padding: '14px 18px' }}>
          <div className="muted" style={{ fontSize: 11.5, fontWeight: 600, textTransform: 'uppercase' }}>
            BIM Objects Available
          </div>
          <div className="num" style={{ fontSize: 28, fontWeight: 700, margin: '6px 0 2px', color: 'var(--grn)' }}>
            100%
          </div>
          <div className="muted" style={{ fontSize: 11.5 }}>
            Parametric IFC 4.3 and Revit families
          </div>
        </div>
      </div>

      {/* Assembly Selector Cards */}
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))',
          gap: 12,
          marginBottom: 24,
        }}
      >
        {PLYWOOD_SYSTEMS.map((system) => {
          const isSelected = system.id === selectedId;
          return (
            <button
              key={system.id}
              type="button"
              onClick={() => setSelectedId(system.id)}
              className="card"
              style={{
                padding: '14px 16px',
                textAlign: 'left',
                cursor: 'pointer',
                borderColor: isSelected ? 'var(--pri)' : 'var(--line)',
                boxShadow: isSelected ? '0 0 0 2px var(--pri-l2)' : 'none',
                background: isSelected ? 'var(--panel)' : 'var(--c0)',
                transition: 'all 0.15s ease',
              }}
            >
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 6 }}>
                <span className="num muted" style={{ fontSize: 10.5, fontWeight: 600 }}>
                  {system.code}
                </span>
                <Chip tone={isSelected ? 'ok' : 'neut'} dot={isSelected}>
                  {system.category}
                </Chip>
              </div>
              <div style={{ fontWeight: 600, fontSize: 13, color: 'var(--ink)', marginBottom: 6 }}>
                {system.name}
              </div>
              <div className="muted" style={{ fontSize: 11.5, lineHeight: 1.4 }}>
                Primary: <strong style={{ color: 'var(--ink2)' }}>{system.primaryPlywoodProduct}</strong>
              </div>
            </button>
          );
        })}
      </div>

      {/* Detailed Assembly Specification Panel */}
      <div className="card" style={{ padding: 24 }}>
        <div
          style={{
            display: 'flex',
            flexWrap: 'wrap',
            justifyContent: 'space-between',
            alignItems: 'flex-start',
            gap: 16,
            borderBottom: '1px solid var(--line)',
            paddingBottom: 20,
            marginBottom: 20,
          }}
        >
          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 4 }}>
              <h2 style={{ margin: 0, fontSize: 18, fontWeight: 700 }}>{activeSystem.name}</h2>
              <Chip tone="ok" dot>
                {activeSystem.moistureClass}
              </Chip>
            </div>
            <div className="muted" style={{ fontSize: 12 }}>
              Assembly System Code: <span className="num font-semibold">{activeSystem.code}</span> • Category:{' '}
              {activeSystem.category}
            </div>
            <p style={{ marginTop: 10, maxWidth: 840, fontSize: 13, lineHeight: 1.5, color: 'var(--ink2)' }}>
              {activeSystem.description}
            </p>
          </div>

          <div style={{ display: 'flex', flexDirection: 'column', gap: 8, minWidth: 220 }}>
            <Link to={`/products/${activeSystem.primaryPlywoodCode}`}>
              <Button style={{ width: '100%' }}>
                View Primary Product ({activeSystem.primaryPlywoodCode})
              </Button>
            </Link>
          </div>
        </div>

        {/* Technical Performance Grid */}
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))',
            gap: 16,
            background: 'var(--c1)',
            padding: 16,
            borderRadius: 'var(--r2)',
            marginBottom: 24,
          }}
        >
          <div>
            <div className="muted" style={{ fontSize: 11 }}>FIRE RESISTANCE</div>
            <div style={{ fontWeight: 600, fontSize: 13, marginTop: 2 }}>{activeSystem.fireRating}</div>
          </div>
          <div>
            <div className="muted" style={{ fontSize: 11 }}>ACOUSTIC PERFORMANCE</div>
            <div style={{ fontWeight: 600, fontSize: 13, marginTop: 2 }}>{activeSystem.acousticRating}</div>
          </div>
          <div>
            <div className="muted" style={{ fontSize: 11 }}>MOISTURE / EXPOSURE</div>
            <div style={{ fontWeight: 600, fontSize: 13, marginTop: 2 }}>{activeSystem.moistureClass}</div>
          </div>
          <div>
            <div className="muted" style={{ fontSize: 11 }}>MANUFACTURER WARRANTY</div>
            <div style={{ fontWeight: 600, fontSize: 13, marginTop: 2, color: 'var(--grn)' }}>
              {activeSystem.warranty}
            </div>
          </div>
        </div>

        {/* Key Engineering Features */}
        <div style={{ marginBottom: 24 }}>
          <h3 style={{ fontSize: 14, fontWeight: 700, margin: '0 0 10px' }}>Key Engineering Highlights</h3>
          <ul style={{ margin: 0, paddingLeft: 20, color: 'var(--ink2)', fontSize: 12.5, lineHeight: 1.6 }}>
            {activeSystem.keyFeatures.map((feat, idx) => (
              <li key={idx} style={{ marginBottom: 4 }}>
                {feat}
              </li>
            ))}
          </ul>
        </div>

        {/* Bill of Materials (BOM) */}
        <div>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 10 }}>
            <h3 style={{ fontSize: 14, fontWeight: 700, margin: 0 }}>
              System Bill of Materials (BOM Breakdown)
            </h3>
            <span className="muted" style={{ fontSize: 12 }}>
              {activeSystem.bom.length} Component Layers
            </span>
          </div>

          <Table>
            <thead>
              <tr>
                <Th>Component Layer</Th>
                <Th>Specification &amp; Dimensions</Th>
                <Th>Quantity / Unit</Th>
                <Th>Functional Role in Assembly</Th>
                <Th>Linked PIM Product</Th>
              </tr>
            </thead>
            <tbody>
              {activeSystem.bom.map((row, idx) => (
                <tr key={idx}>
                  <Td style={{ fontWeight: 600 }}>{row.item}</Td>
                  <Td>{row.specification}</Td>
                  <Td className="num">{row.qty}</Td>
                  <Td className="muted" style={{ fontSize: 12 }}>
                    {row.role}
                  </Td>
                  <Td>
                    {row.productCode ? (
                      <Link to={`/products/${row.productCode}`}>
                        <Chip tone="ok" dot>
                          {row.productCode}
                        </Chip>
                      </Link>
                    ) : (
                      <span className="muted" style={{ fontSize: 11.5 }}>
                        Architectural Hardware
                      </span>
                    )}
                  </Td>
                </tr>
              ))}
            </tbody>
          </Table>
        </div>
      </div>
    </div>
  );
}
