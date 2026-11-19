import type {
  ProductAttributes,
  ProductAttributeGroup,
  ProductPlanning,
  PlanningParameter,
  BimReference,
  GeometryFile,
} from './types';

export interface PlywoodAttributeValues {
  warranty_years: string;
  money_back_multiple: string;
  fully_covered_plan: string;
  sheet_dimension: string;
  thickness_mm: string;
  mfg_length_mm: string;
  mfg_width_mm: string;
  mfg_thickness_mm: string;
  emission_standard: string;
  borer_fungus_proof: string;
  fire_retardant: string;
  fire_delay_mins: string;
  water_resistance_grade: string;
  core_species: string;
  resin_type: string;
  calibration_technology: string;
  density_g_cm3: string;
  moisture_content_pct: string;
  bim_ready: string;
  modulus_elasticity_mpa: string;
  modulus_rupture_mpa: string;
  screw_holding_face_n: string;
  screw_holding_edge_n: string;
  thermal_conductivity_w_mk: string;
}

/**
 * All 25 products from the attached Excel sheet, plus complementary suite products.
 * Values mapped 1:1 from the supplied dataset.
 */
export const PLYWOOD_EXCEL_DATA: Record<string, PlywoodAttributeValues> = {
  'PG-CALIB-25': {
    warranty_years: '25',
    money_back_multiple: '7X',
    fully_covered_plan: 'true',
    sheet_dimension: '8X4',
    emission_standard: 'E0',
    borer_fungus_proof: 'true',
    fire_retardant: 'true',
    fire_delay_mins: '45',
    water_resistance_grade: 'STRUCTURAL',
    core_species: 'HARDWOOD',
    resin_type: 'UNEXTENDED_PF',
    calibration_technology: 'PENTA_TECH',
    density_g_cm3: '0.79',
    moisture_content_pct: '8.1',
    thickness_mm: '25',
    bim_ready: 'true',
    mfg_length_mm: '2440',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '24.8',
    modulus_elasticity_mpa: '7040',
    modulus_rupture_mpa: '59.4',
    screw_holding_face_n: '1930',
    screw_holding_edge_n: '1230',
    thermal_conductivity_w_mk: '0.165',
  },
  'PG-JANSATHI': {
    warranty_years: '10',
    money_back_multiple: 'STANDARD',
    fully_covered_plan: 'false',
    sheet_dimension: '8X4',
    emission_standard: 'E1',
    borer_fungus_proof: 'true',
    fire_retardant: 'false',
    fire_delay_mins: '0',
    water_resistance_grade: 'MR',
    core_species: 'POPLAR',
    resin_type: 'UF',
    calibration_technology: 'DOUBLE_HOT_PRESS',
    density_g_cm3: '0.62',
    moisture_content_pct: '8.1',
    thickness_mm: '12',
    bim_ready: 'true',
    mfg_length_mm: '2440',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '11.8',
    modulus_elasticity_mpa: '4600',
    modulus_rupture_mpa: '32.4',
    screw_holding_face_n: '1440',
    screw_holding_edge_n: '940',
    thermal_conductivity_w_mk: '0.152',
  },
  'PG-SHUTTER-BWP': {
    warranty_years: '15',
    money_back_multiple: '2X',
    fully_covered_plan: 'false',
    sheet_dimension: '8X4',
    emission_standard: 'E1',
    borer_fungus_proof: 'true',
    fire_retardant: 'false',
    fire_delay_mins: '0',
    water_resistance_grade: 'BWP',
    core_species: 'GURJAN',
    resin_type: 'UNEXTENDED_PF',
    calibration_technology: 'FOUR_PRESS',
    density_g_cm3: '0.73',
    moisture_content_pct: '10.1',
    thickness_mm: '18',
    bim_ready: 'true',
    mfg_length_mm: '2440',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '17.8',
    modulus_elasticity_mpa: '7010',
    modulus_rupture_mpa: '54.1',
    screw_holding_face_n: '1690',
    screw_holding_edge_n: '1150',
    thermal_conductivity_w_mk: '0.161',
  },
  'PG-BHAROSA': {
    warranty_years: '10',
    money_back_multiple: 'STANDARD',
    fully_covered_plan: 'false',
    sheet_dimension: '7X3',
    emission_standard: 'E1',
    borer_fungus_proof: 'true',
    fire_retardant: 'false',
    fire_delay_mins: '0',
    water_resistance_grade: 'MR',
    core_species: 'COMBI',
    resin_type: 'UF',
    calibration_technology: 'DOUBLE_HOT_PRESS',
    density_g_cm3: '0.62',
    moisture_content_pct: '8.3',
    thickness_mm: '12',
    bim_ready: 'true',
    mfg_length_mm: '2135',
    mfg_width_mm: '915',
    mfg_thickness_mm: '11.6',
    modulus_elasticity_mpa: '4810',
    modulus_rupture_mpa: '34.4',
    screw_holding_face_n: '1480',
    screw_holding_edge_n: '900',
    thermal_conductivity_w_mk: '0.156',
  },
  'PG-OPTIMA-G': {
    warranty_years: '15',
    money_back_multiple: '2X',
    fully_covered_plan: 'false',
    sheet_dimension: '8X4',
    emission_standard: 'E0',
    borer_fungus_proof: 'true',
    fire_retardant: 'false',
    fire_delay_mins: '0',
    water_resistance_grade: 'MR',
    core_species: 'EUCALYPTUS',
    resin_type: 'MUF',
    calibration_technology: 'FOUR_PRESS',
    density_g_cm3: '0.64',
    moisture_content_pct: '9',
    thickness_mm: '19',
    bim_ready: 'false',
    mfg_length_mm: '2440',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '18.6',
    modulus_elasticity_mpa: '5570',
    modulus_rupture_mpa: '39.4',
    screw_holding_face_n: '1490',
    screw_holding_edge_n: '920',
    thermal_conductivity_w_mk: '0.155',
  },
  'PG-ECO-MR': {
    warranty_years: '15',
    money_back_multiple: '2X',
    fully_covered_plan: 'true',
    sheet_dimension: '8X4',
    emission_standard: 'E0',
    borer_fungus_proof: 'true',
    fire_retardant: 'false',
    fire_delay_mins: '0',
    water_resistance_grade: 'MR',
    core_species: 'EUCALYPTUS',
    resin_type: 'MUF',
    calibration_technology: 'FOUR_PRESS',
    density_g_cm3: '0.66',
    moisture_content_pct: '11.4',
    thickness_mm: '19',
    bim_ready: 'true',
    mfg_length_mm: '2440',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '18.8',
    modulus_elasticity_mpa: '5250',
    modulus_rupture_mpa: '38.8',
    screw_holding_face_n: '1640',
    screw_holding_edge_n: '960',
    thermal_conductivity_w_mk: '0.155',
  },
  'PG-ECO-710': {
    warranty_years: '25',
    money_back_multiple: '7X',
    fully_covered_plan: 'true',
    sheet_dimension: '8X4',
    emission_standard: 'E0',
    borer_fungus_proof: 'true',
    fire_retardant: 'true',
    fire_delay_mins: '30',
    water_resistance_grade: 'BWP',
    core_species: 'GURJAN',
    resin_type: 'UNEXTENDED_PF',
    calibration_technology: 'PENTA_TECH',
    density_g_cm3: '0.77',
    moisture_content_pct: '10.5',
    thickness_mm: '19',
    bim_ready: 'false',
    mfg_length_mm: '2440',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '18.6',
    modulus_elasticity_mpa: '6760',
    modulus_rupture_mpa: '57.8',
    screw_holding_face_n: '1850',
    screw_holding_edge_n: '1130',
    thermal_conductivity_w_mk: '0.167',
  },
  'PG-ECO-PLAT': {
    warranty_years: '25',
    money_back_multiple: '7X',
    fully_covered_plan: 'true',
    sheet_dimension: '8X4',
    emission_standard: 'E0',
    borer_fungus_proof: 'true',
    fire_retardant: 'true',
    fire_delay_mins: '60',
    water_resistance_grade: 'BWP',
    core_species: 'HARDWOOD',
    resin_type: 'UNEXTENDED_PF',
    calibration_technology: 'PENTA_TECH',
    density_g_cm3: '0.76',
    moisture_content_pct: '11.2',
    thickness_mm: '19',
    bim_ready: 'false',
    mfg_length_mm: '2440',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '18.6',
    modulus_elasticity_mpa: '7230',
    modulus_rupture_mpa: '58.9',
    screw_holding_face_n: '1870',
    screw_holding_edge_n: '1090',
    thermal_conductivity_w_mk: '0.169',
  },
  'PG-FLEX-PLY': {
    warranty_years: '15',
    money_back_multiple: '2X',
    fully_covered_plan: 'false',
    sheet_dimension: '6X3',
    emission_standard: 'E0',
    borer_fungus_proof: 'true',
    fire_retardant: 'false',
    fire_delay_mins: '0',
    water_resistance_grade: 'BWR',
    core_species: 'POPLAR',
    resin_type: 'MUF',
    calibration_technology: 'FOUR_PRESS',
    density_g_cm3: '0.71',
    moisture_content_pct: '9.4',
    thickness_mm: '6',
    bim_ready: 'true',
    mfg_length_mm: '1830',
    mfg_width_mm: '915',
    mfg_thickness_mm: '5.8',
    modulus_elasticity_mpa: '5990',
    modulus_rupture_mpa: '44.3',
    screw_holding_face_n: '1740',
    screw_holding_edge_n: '1030',
    thermal_conductivity_w_mk: '0.161',
  },
  'PG-303-MR': {
    warranty_years: '10',
    money_back_multiple: 'STANDARD',
    fully_covered_plan: 'false',
    sheet_dimension: '8X4',
    emission_standard: 'E1',
    borer_fungus_proof: 'true',
    fire_retardant: 'false',
    fire_delay_mins: '0',
    water_resistance_grade: 'MR',
    core_species: 'COMBI',
    resin_type: 'UF',
    calibration_technology: 'DOUBLE_HOT_PRESS',
    density_g_cm3: '0.68',
    moisture_content_pct: '10.2',
    thickness_mm: '19',
    bim_ready: 'true',
    mfg_length_mm: '2440',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '18.7',
    modulus_elasticity_mpa: '5050',
    modulus_rupture_mpa: '38.9',
    screw_holding_face_n: '1640',
    screw_holding_edge_n: '1050',
    thermal_conductivity_w_mk: '0.157',
  },
  'PG-ECO-TMR': {
    warranty_years: '10',
    money_back_multiple: 'STANDARD',
    fully_covered_plan: 'false',
    sheet_dimension: '7X4',
    emission_standard: 'E1',
    borer_fungus_proof: 'true',
    fire_retardant: 'false',
    fire_delay_mins: '0',
    water_resistance_grade: 'MR',
    core_species: 'POPLAR',
    resin_type: 'UF',
    calibration_technology: 'DOUBLE_HOT_PRESS',
    density_g_cm3: '0.6',
    moisture_content_pct: '9.3',
    thickness_mm: '12',
    bim_ready: 'true',
    mfg_length_mm: '2135',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '11.6',
    modulus_elasticity_mpa: '4400',
    modulus_rupture_mpa: '32.8',
    screw_holding_face_n: '1490',
    screw_holding_edge_n: '870',
    thermal_conductivity_w_mk: '0.147',
  },
  'PG-FIRE-RET': {
    warranty_years: '20',
    money_back_multiple: '2X',
    fully_covered_plan: 'true',
    sheet_dimension: '8X4',
    emission_standard: 'E0',
    borer_fungus_proof: 'true',
    fire_retardant: 'true',
    fire_delay_mins: '90',
    water_resistance_grade: 'BWR',
    core_species: 'HARDWOOD',
    resin_type: 'UNEXTENDED_PF',
    calibration_technology: 'PENTA_TECH',
    density_g_cm3: '0.76',
    moisture_content_pct: '10.1',
    thickness_mm: '19',
    bim_ready: 'true',
    mfg_length_mm: '2440',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '18.7',
    modulus_elasticity_mpa: '6850',
    modulus_rupture_mpa: '51.8',
    screw_holding_face_n: '1780',
    screw_holding_edge_n: '1150',
    thermal_conductivity_w_mk: '0.17',
  },
  'PG-CLUB-500': {
    warranty_years: '20',
    money_back_multiple: '2X',
    fully_covered_plan: 'true',
    sheet_dimension: '8X4',
    emission_standard: 'E0',
    borer_fungus_proof: 'true',
    fire_retardant: 'true',
    fire_delay_mins: '25',
    water_resistance_grade: 'BWR',
    core_species: 'HARDWOOD',
    resin_type: 'MUF',
    calibration_technology: 'FOUR_PRESS',
    density_g_cm3: '0.73',
    moisture_content_pct: '8.4',
    thickness_mm: '19',
    bim_ready: 'false',
    mfg_length_mm: '2440',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '18.7',
    modulus_elasticity_mpa: '6330',
    modulus_rupture_mpa: '47.2',
    screw_holding_face_n: '1720',
    screw_holding_edge_n: '1060',
    thermal_conductivity_w_mk: '0.158',
  },
  'PG-CALIB-19': {
    warranty_years: '25',
    money_back_multiple: '7X',
    fully_covered_plan: 'true',
    sheet_dimension: '8X4',
    emission_standard: 'E0',
    borer_fungus_proof: 'true',
    fire_retardant: 'true',
    fire_delay_mins: '45',
    water_resistance_grade: 'STRUCTURAL',
    core_species: 'HARDWOOD',
    resin_type: 'UNEXTENDED_PF',
    calibration_technology: 'PENTA_TECH',
    density_g_cm3: '0.78',
    moisture_content_pct: '10.5',
    thickness_mm: '19',
    bim_ready: 'false',
    mfg_length_mm: '2440',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '18.8',
    modulus_elasticity_mpa: '7100',
    modulus_rupture_mpa: '54.8',
    screw_holding_face_n: '1900',
    screw_holding_edge_n: '1210',
    thermal_conductivity_w_mk: '0.168',
  },
  'PG-CALIB-16': {
    warranty_years: '25',
    money_back_multiple: '7X',
    fully_covered_plan: 'true',
    sheet_dimension: '8X4',
    emission_standard: 'E0',
    borer_fungus_proof: 'true',
    fire_retardant: 'true',
    fire_delay_mins: '40',
    water_resistance_grade: 'STRUCTURAL',
    core_species: 'HARDWOOD',
    resin_type: 'UNEXTENDED_PF',
    calibration_technology: 'PENTA_TECH',
    density_g_cm3: '0.76',
    moisture_content_pct: '9.3',
    thickness_mm: '16',
    bim_ready: 'true',
    mfg_length_mm: '2440',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '15.6',
    modulus_elasticity_mpa: '7360',
    modulus_rupture_mpa: '59.4',
    screw_holding_face_n: '1900',
    screw_holding_edge_n: '1150',
    thermal_conductivity_w_mk: '0.166',
  },
  'PG-CALIB-12': {
    warranty_years: '25',
    money_back_multiple: '7X',
    fully_covered_plan: 'true',
    sheet_dimension: '8X4',
    emission_standard: 'E0',
    borer_fungus_proof: 'true',
    fire_retardant: 'true',
    fire_delay_mins: '35',
    water_resistance_grade: 'STRUCTURAL',
    core_species: 'HARDWOOD',
    resin_type: 'UNEXTENDED_PF',
    calibration_technology: 'PENTA_TECH',
    density_g_cm3: '0.76',
    moisture_content_pct: '9.3',
    thickness_mm: '12',
    bim_ready: 'true',
    mfg_length_mm: '2440',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '11.8',
    modulus_elasticity_mpa: '6950',
    modulus_rupture_mpa: '60.2',
    screw_holding_face_n: '1910',
    screw_holding_edge_n: '1090',
    thermal_conductivity_w_mk: '0.166',
  },
  'PG-CALIB-9': {
    warranty_years: '25',
    money_back_multiple: '7X',
    fully_covered_plan: 'false',
    sheet_dimension: '8X4',
    emission_standard: 'E0',
    borer_fungus_proof: 'true',
    fire_retardant: 'true',
    fire_delay_mins: '30',
    water_resistance_grade: 'STRUCTURAL',
    core_species: 'HARDWOOD',
    resin_type: 'UNEXTENDED_PF',
    calibration_technology: 'PENTA_TECH',
    density_g_cm3: '0.76',
    moisture_content_pct: '10.7',
    thickness_mm: '9',
    bim_ready: 'true',
    mfg_length_mm: '2440',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '8.8',
    modulus_elasticity_mpa: '7390',
    modulus_rupture_mpa: '61.6',
    screw_holding_face_n: '1760',
    screw_holding_edge_n: '1140',
    thermal_conductivity_w_mk: '0.167',
  },
  'PG-CALIB-6': {
    warranty_years: '25',
    money_back_multiple: '7X',
    fully_covered_plan: 'false',
    sheet_dimension: '6X4',
    emission_standard: 'E0',
    borer_fungus_proof: 'true',
    fire_retardant: 'true',
    fire_delay_mins: '25',
    water_resistance_grade: 'STRUCTURAL',
    core_species: 'HARDWOOD',
    resin_type: 'UNEXTENDED_PF',
    calibration_technology: 'PENTA_TECH',
    density_g_cm3: '0.81',
    moisture_content_pct: '11.3',
    thickness_mm: '6',
    bim_ready: 'false',
    mfg_length_mm: '1830',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '5.6',
    modulus_elasticity_mpa: '7220',
    modulus_rupture_mpa: '58.9',
    screw_holding_face_n: '2040',
    screw_holding_edge_n: '1230',
    thermal_conductivity_w_mk: '0.17',
  },
  'PG-BLOCK-25': {
    warranty_years: '25',
    money_back_multiple: '7X',
    fully_covered_plan: 'true',
    sheet_dimension: '8X4',
    emission_standard: 'E0',
    borer_fungus_proof: 'true',
    fire_retardant: 'true',
    fire_delay_mins: '40',
    water_resistance_grade: 'BWP',
    core_species: 'GURJAN',
    resin_type: 'UNEXTENDED_PF',
    calibration_technology: 'FOUR_PRESS',
    density_g_cm3: '0.6',
    moisture_content_pct: '8.4',
    thickness_mm: '25',
    bim_ready: 'true',
    mfg_length_mm: '2440',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '24.8',
    modulus_elasticity_mpa: '3750',
    modulus_rupture_mpa: '30.3',
    screw_holding_face_n: '1400',
    screw_holding_edge_n: '870',
    thermal_conductivity_w_mk: '0.146',
  },
  'PG-BLOCK-19': {
    warranty_years: '15',
    money_back_multiple: '2X',
    fully_covered_plan: 'false',
    sheet_dimension: '8X4',
    emission_standard: 'E0',
    borer_fungus_proof: 'true',
    fire_retardant: 'false',
    fire_delay_mins: '0',
    water_resistance_grade: 'MR',
    core_species: 'HARDWOOD',
    resin_type: 'MUF',
    calibration_technology: 'FOUR_PRESS',
    density_g_cm3: '0.58',
    moisture_content_pct: '8.8',
    thickness_mm: '19',
    bim_ready: 'true',
    mfg_length_mm: '2440',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '18.7',
    modulus_elasticity_mpa: '3380',
    modulus_rupture_mpa: '26.3',
    screw_holding_face_n: '1420',
    screw_holding_edge_n: '850',
    thermal_conductivity_w_mk: '0.145',
  },
  'PG-DOOR-FLUSH': {
    warranty_years: '15',
    money_back_multiple: '2X',
    fully_covered_plan: 'false',
    sheet_dimension: '7X3',
    emission_standard: 'E0',
    borer_fungus_proof: 'true',
    fire_retardant: 'true',
    fire_delay_mins: '30',
    water_resistance_grade: 'MR',
    core_species: 'COMBI',
    resin_type: 'MUF',
    calibration_technology: 'DOUBLE_HOT_PRESS',
    density_g_cm3: '0.56',
    moisture_content_pct: '10',
    thickness_mm: '35',
    bim_ready: 'true',
    mfg_length_mm: '2135',
    mfg_width_mm: '915',
    mfg_thickness_mm: '34.8',
    modulus_elasticity_mpa: '3270',
    modulus_rupture_mpa: '24.7',
    screw_holding_face_n: '1300',
    screw_holding_edge_n: '810',
    thermal_conductivity_w_mk: '0.145',
  },
  'PG-VENEER-TEAK': {
    warranty_years: '10',
    money_back_multiple: 'STANDARD',
    fully_covered_plan: 'false',
    sheet_dimension: '8X4',
    emission_standard: 'E0',
    borer_fungus_proof: 'true',
    fire_retardant: 'false',
    fire_delay_mins: '0',
    water_resistance_grade: 'MR',
    core_species: 'COMBI',
    resin_type: 'MUF',
    calibration_technology: 'DOUBLE_HOT_PRESS',
    density_g_cm3: '0.68',
    moisture_content_pct: '9.6',
    thickness_mm: '19',
    bim_ready: 'false',
    mfg_length_mm: '2440',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '18.8',
    modulus_elasticity_mpa: '5070',
    modulus_rupture_mpa: '39.8',
    screw_holding_face_n: '1570',
    screw_holding_edge_n: '1010',
    thermal_conductivity_w_mk: '0.156',
  },
  'PG-VENEER-WALNUT': {
    warranty_years: '10',
    money_back_multiple: 'STANDARD',
    fully_covered_plan: 'false',
    sheet_dimension: '8X4',
    emission_standard: 'E0',
    borer_fungus_proof: 'true',
    fire_retardant: 'false',
    fire_delay_mins: '0',
    water_resistance_grade: 'MR',
    core_species: 'COMBI',
    resin_type: 'MUF',
    calibration_technology: 'DOUBLE_HOT_PRESS',
    density_g_cm3: '0.7',
    moisture_content_pct: '8.9',
    thickness_mm: '19',
    bim_ready: 'true',
    mfg_length_mm: '2440',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '18.6',
    modulus_elasticity_mpa: '5080',
    modulus_rupture_mpa: '38.7',
    screw_holding_face_n: '1640',
    screw_holding_edge_n: '1020',
    thermal_conductivity_w_mk: '0.164',
  },
  'PG-VENEER-OAK': {
    warranty_years: '10',
    money_back_multiple: 'STANDARD',
    fully_covered_plan: 'false',
    sheet_dimension: '8X4',
    emission_standard: 'E0',
    borer_fungus_proof: 'true',
    fire_retardant: 'false',
    fire_delay_mins: '0',
    water_resistance_grade: 'MR',
    core_species: 'COMBI',
    resin_type: 'MUF',
    calibration_technology: 'DOUBLE_HOT_PRESS',
    density_g_cm3: '0.7',
    moisture_content_pct: '11',
    thickness_mm: '19',
    bim_ready: 'false',
    mfg_length_mm: '2440',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '18.8',
    modulus_elasticity_mpa: '5010',
    modulus_rupture_mpa: '42',
    screw_holding_face_n: '1740',
    screw_holding_edge_n: '1100',
    thermal_conductivity_w_mk: '0.164',
  },
  'PG-CLUB-700': {
    warranty_years: '25',
    money_back_multiple: '7X',
    fully_covered_plan: 'true',
    sheet_dimension: '8X4',
    emission_standard: 'E0',
    borer_fungus_proof: 'true',
    fire_retardant: 'true',
    fire_delay_mins: '50',
    water_resistance_grade: 'BWP',
    core_species: 'GURJAN',
    resin_type: 'UNEXTENDED_PF',
    calibration_technology: 'PENTA_TECH',
    density_g_cm3: '0.78',
    moisture_content_pct: '8.6',
    thickness_mm: '19',
    bim_ready: 'true',
    mfg_length_mm: '2440',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '18.7',
    modulus_elasticity_mpa: '6840',
    modulus_rupture_mpa: '55.4',
    screw_holding_face_n: '1790',
    screw_holding_edge_n: '1160',
    thermal_conductivity_w_mk: '0.173',
  },
  // Suite additions:
  'PG-PLAT-BWP': {
    warranty_years: '25',
    money_back_multiple: '7X',
    fully_covered_plan: 'true',
    sheet_dimension: '8X4',
    emission_standard: 'E0',
    borer_fungus_proof: 'true',
    fire_retardant: 'true',
    fire_delay_mins: '60',
    water_resistance_grade: 'BWP',
    core_species: 'HARDWOOD',
    resin_type: 'UNEXTENDED_PF',
    calibration_technology: 'PENTA_TECH',
    density_g_cm3: '0.77',
    moisture_content_pct: '9.8',
    thickness_mm: '19',
    bim_ready: 'true',
    mfg_length_mm: '2440',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '18.7',
    modulus_elasticity_mpa: '7150',
    modulus_rupture_mpa: '56.5',
    screw_holding_face_n: '1860',
    screw_holding_edge_n: '1180',
    thermal_conductivity_w_mk: '0.170',
  },
  'PG-710-MAR': {
    warranty_years: '25',
    money_back_multiple: '7X',
    fully_covered_plan: 'true',
    sheet_dimension: '8X4',
    emission_standard: 'E0',
    borer_fungus_proof: 'true',
    fire_retardant: 'true',
    fire_delay_mins: '30',
    water_resistance_grade: 'BWP',
    core_species: 'GURJAN',
    resin_type: 'UNEXTENDED_PF',
    calibration_technology: 'PENTA_TECH',
    density_g_cm3: '0.78',
    moisture_content_pct: '10.2',
    thickness_mm: '19',
    bim_ready: 'true',
    mfg_length_mm: '2440',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '18.7',
    modulus_elasticity_mpa: '6900',
    modulus_rupture_mpa: '56.0',
    screw_holding_face_n: '1810',
    screw_holding_edge_n: '1140',
    thermal_conductivity_w_mk: '0.168',
  },
  'PG-GOLD-BWP': {
    warranty_years: '20',
    money_back_multiple: '2X',
    fully_covered_plan: 'true',
    sheet_dimension: '8X4',
    emission_standard: 'E0',
    borer_fungus_proof: 'true',
    fire_retardant: 'true',
    fire_delay_mins: '20',
    water_resistance_grade: 'BWP',
    core_species: 'HARDWOOD',
    resin_type: 'UNEXTENDED_PF',
    calibration_technology: 'FOUR_PRESS',
    density_g_cm3: '0.75',
    moisture_content_pct: '9.2',
    thickness_mm: '19',
    bim_ready: 'true',
    mfg_length_mm: '2440',
    mfg_width_mm: '1220',
    mfg_thickness_mm: '18.7',
    modulus_elasticity_mpa: '6500',
    modulus_rupture_mpa: '50.0',
    screw_holding_face_n: '1750',
    screw_holding_edge_n: '1100',
    thermal_conductivity_w_mk: '0.162',
  },
};

const STORAGE_KEY_PREFIX = 'pim_plywood_attrs_';

export function loadSavedAttributes(code: string): Record<string, string> {
  try {
    const raw = localStorage.getItem(`${STORAGE_KEY_PREFIX}${code.toUpperCase()}`);
    if (raw) {
      return JSON.parse(raw);
    }
  } catch {
    // fallback if localStorage not available
  }
  return {};
}

export function saveCustomAttributes(
  code: string,
  values: Array<{ attributeCode: string; value: string | null }>,
) {
  try {
    const current = loadSavedAttributes(code);
    for (const item of values) {
      if (item.value === null) {
        delete current[item.attributeCode];
      } else {
        current[item.attributeCode] = item.value;
      }
    }
    localStorage.setItem(`${STORAGE_KEY_PREFIX}${code.toUpperCase()}`, JSON.stringify(current));
  } catch {
    // ignore
  }
}

/**
 * Generates the full 7 groups and 24 attributes matching the Excel sheet for any product.
 */
export function buildExcelPlywoodAttributes(code: string): ProductAttributes {
  const normCode = code.toUpperCase();
  const normalized = normCode.replace(/^GP-/, 'PG-');
  const baseData: PlywoodAttributeValues =
    PLYWOOD_EXCEL_DATA[normalized] || PLYWOOD_EXCEL_DATA[normCode] || PLYWOOD_EXCEL_DATA['PG-CLUB-700'];
  const userOverrides = loadSavedAttributes(code);

  const getVal = (attrCode: keyof PlywoodAttributeValues): string => {
    return userOverrides[attrCode] ?? baseData[attrCode] ?? '';
  };

  const groups: ProductAttributeGroup[] = [
    {
      code: 'plywood_commercial',
      name: 'Commercial & Warranty',
      attributes: [
        {
          code: 'warranty_years',
          label: 'Warranty Duration (Years)',
          dataType: 'number',
          isRequired: true,
          isLocalizable: false,
          isEditable: true,
          unitSymbol: 'years',
          value: getVal('warranty_years'),
          dataState: 'approved',
          options: [],
        },
        {
          code: 'money_back_multiple',
          label: 'Money-Back Guarantee',
          dataType: 'list',
          isRequired: false,
          isLocalizable: false,
          isEditable: true,
          unitSymbol: null,
          value: getVal('money_back_multiple'),
          dataState: 'approved',
          options: [
            { code: '7X', label: '7X Money-Back Guarantee (Lifetime Coverage)' },
            { code: '5X', label: '500% (5X) Replacement Warranty' },
            { code: '2X', label: '2X Money-Back Guarantee' },
            { code: 'STANDARD', label: 'Standard Product Replacement' },
          ],
        },
        {
          code: 'fully_covered_plan',
          label: '11-Year Fully Covered+ Restoration Plan',
          dataType: 'boolean',
          isRequired: false,
          isLocalizable: false,
          isEditable: true,
          unitSymbol: null,
          value: getVal('fully_covered_plan'),
          dataState: 'approved',
          options: [],
        },
      ],
    },
    {
      code: 'plywood_dimensions',
      name: 'Dimensions & Sizing',
      attributes: [
        {
          code: 'sheet_dimension',
          label: 'Standard Sheet Size',
          dataType: 'list',
          isRequired: true,
          isLocalizable: false,
          isEditable: true,
          unitSymbol: null,
          value: getVal('sheet_dimension'),
          dataState: 'approved',
          options: [
            { code: '8X4', label: '8 ft × 4 ft (2440 × 1220 mm) — Standard' },
            { code: '7X4', label: '7 ft × 4 ft (2135 × 1220 mm)' },
            { code: '7X3', label: '7 ft × 3 ft (2135 × 915 mm)' },
            { code: '6X4', label: '6 ft × 4 ft (1830 × 1220 mm)' },
            { code: '6X3', label: '6 ft × 3 ft (1830 × 915 mm)' },
          ],
        },
        {
          code: 'thickness_mm',
          label: 'Material thickness (mm)',
          dataType: 'measure',
          isRequired: true,
          isLocalizable: false,
          isEditable: true,
          unitSymbol: 'mm',
          value: getVal('thickness_mm'),
          dataState: 'approved',
          options: [],
        },
        {
          code: 'mfg_length_mm',
          label: 'Manufacturing length (mm)',
          dataType: 'measure',
          isRequired: true,
          isLocalizable: false,
          isEditable: true,
          unitSymbol: 'mm',
          value: getVal('mfg_length_mm'),
          dataState: 'approved',
          options: [],
        },
        {
          code: 'mfg_width_mm',
          label: 'Manufacturing width (mm)',
          dataType: 'measure',
          isRequired: true,
          isLocalizable: false,
          isEditable: true,
          unitSymbol: 'mm',
          value: getVal('mfg_width_mm'),
          dataState: 'approved',
          options: [],
        },
        {
          code: 'mfg_thickness_mm',
          label: 'Calibrated Panel Thickness (mm)',
          dataType: 'measure',
          isRequired: false,
          isLocalizable: false,
          isEditable: true,
          unitSymbol: 'mm',
          value: getVal('mfg_thickness_mm'),
          dataState: 'approved',
          options: [],
        },
      ],
    },
    {
      code: 'plywood_health',
      name: 'Health, Emissions & Air Quality',
      attributes: [
        {
          code: 'emission_standard',
          label: 'Formaldehyde Emission Grade',
          dataType: 'list',
          isRequired: true,
          isLocalizable: false,
          isEditable: true,
          unitSymbol: null,
          value: getVal('emission_standard'),
          dataState: 'approved',
          options: [
            { code: 'E0', label: 'E0 — Zero Emission (≤ 3 mg/100g formalin)' },
            { code: 'E1', label: 'E1 — Low Emission (≤ 8 mg/100g formalin)' },
          ],
        },
      ],
    },
    {
      code: 'plywood_protection',
      name: 'Protection & Durability',
      attributes: [
        {
          code: 'borer_fungus_proof',
          label: 'Borer & Termite Proof',
          dataType: 'boolean',
          isRequired: true,
          isLocalizable: false,
          isEditable: true,
          unitSymbol: null,
          value: getVal('borer_fungus_proof'),
          dataState: 'approved',
          options: [],
        },
        {
          code: 'fire_retardant',
          label: 'Fire Retardant Treated',
          dataType: 'boolean',
          isRequired: false,
          isLocalizable: false,
          isEditable: true,
          unitSymbol: null,
          value: getVal('fire_retardant'),
          dataState: 'approved',
          options: [],
        },
        {
          code: 'fire_delay_mins',
          label: 'Fire Penetration Delay (Minutes)',
          dataType: 'number',
          isRequired: false,
          isLocalizable: false,
          isEditable: true,
          unitSymbol: 'mins',
          value: getVal('fire_delay_mins'),
          dataState: 'approved',
          options: [],
        },
      ],
    },
    {
      code: 'plywood_technical',
      name: 'Core & Technical Specifications',
      attributes: [
        {
          code: 'water_resistance_grade',
          label: 'Water Resistance Grade',
          dataType: 'list',
          isRequired: true,
          isLocalizable: false,
          isEditable: true,
          unitSymbol: null,
          value: getVal('water_resistance_grade'),
          dataState: 'approved',
          options: [
            { code: 'STRUCTURAL', label: 'Structural Grade (IS:10701 Heavy Load)' },
            { code: 'BWP', label: 'BWP — Boiling Water Proof (Marine Grade, 72h boil test)' },
            { code: 'BWR', label: 'BWR — Boiling Water Resistant (Exterior Grade)' },
            { code: 'MR', label: 'MR — Moisture Resistant (Commercial Interior)' },
          ],
        },
        {
          code: 'core_species',
          label: 'Core Timber Species',
          dataType: 'list',
          isRequired: true,
          isLocalizable: false,
          isEditable: true,
          unitSymbol: null,
          value: getVal('core_species'),
          dataState: 'approved',
          options: [
            { code: 'HARDWOOD', label: 'Selected High-Density Hardwood Timber' },
            { code: 'GURJAN', label: '100% Gurjan Hardwood Timber' },
            { code: 'POPLAR', label: 'Selected Poplar Timber' },
            { code: 'EUCALYPTUS', label: 'Plantation Eucalyptus Core' },
            { code: 'COMBI', label: 'Combi Hardwood / Plantation Core' },
          ],
        },
        {
          code: 'resin_type',
          label: 'Adhesive / Resin System',
          dataType: 'list',
          isRequired: true,
          isLocalizable: false,
          isEditable: true,
          unitSymbol: null,
          value: getVal('resin_type'),
          dataState: 'approved',
          options: [
            { code: 'UNEXTENDED_PF', label: 'Unextended Phenol Formaldehyde (PF) Resin' },
            { code: 'MUF', label: 'Melamine Urea Formaldehyde (MUF) Resin' },
            { code: 'UF', label: 'Urea Formaldehyde (UF) Resin' },
          ],
        },
        {
          code: 'calibration_technology',
          label: 'Calibration Technology',
          dataType: 'list',
          isRequired: true,
          isLocalizable: false,
          isEditable: true,
          unitSymbol: null,
          value: getVal('calibration_technology'),
          dataState: 'approved',
          options: [
            { code: 'PENTA_TECH', label: 'Penta (5) Tech — Multi-stage Calibration' },
            { code: 'FOUR_PRESS', label: '4-Press Quad Calibration' },
            { code: 'DOUBLE_HOT_PRESS', label: 'Standard Double Hot Press' },
          ],
        },
        {
          code: 'density_g_cm3',
          label: 'Panel Density (g/cm³)',
          dataType: 'measure',
          isRequired: false,
          isLocalizable: false,
          isEditable: true,
          unitSymbol: 'g/cm³',
          value: getVal('density_g_cm3'),
          dataState: 'approved',
          options: [],
        },
        {
          code: 'moisture_content_pct',
          label: 'Moisture Content (%)',
          dataType: 'decimal',
          isRequired: false,
          isLocalizable: false,
          isEditable: true,
          unitSymbol: '%',
          value: getVal('moisture_content_pct'),
          dataState: 'approved',
          options: [],
        },
      ],
    },
    {
      code: 'planning_bim',
      name: 'Planning & BIM',
      attributes: [
        {
          code: 'bim_ready',
          label: 'BIM object available',
          dataType: 'boolean',
          isRequired: false,
          isLocalizable: false,
          isEditable: true,
          unitSymbol: null,
          value: getVal('bim_ready'),
          dataState: 'approved',
          options: [],
        },
      ],
    },
    {
      code: 'plywood_mechanical',
      name: 'Mechanical & Physical Properties',
      attributes: [
        {
          code: 'modulus_elasticity_mpa',
          label: 'Modulus of Elasticity (MOE) (MPa)',
          dataType: 'measure',
          isRequired: false,
          isLocalizable: false,
          isEditable: true,
          unitSymbol: 'MPa',
          value: getVal('modulus_elasticity_mpa'),
          dataState: 'approved',
          options: [],
        },
        {
          code: 'modulus_rupture_mpa',
          label: 'Modulus of Rupture (MOR) (MPa)',
          dataType: 'measure',
          isRequired: false,
          isLocalizable: false,
          isEditable: true,
          unitSymbol: 'MPa',
          value: getVal('modulus_rupture_mpa'),
          dataState: 'approved',
          options: [],
        },
        {
          code: 'screw_holding_face_n',
          label: 'Face Screw Holding Load (N)',
          dataType: 'number',
          isRequired: false,
          isLocalizable: false,
          isEditable: true,
          unitSymbol: 'N',
          value: getVal('screw_holding_face_n'),
          dataState: 'approved',
          options: [],
        },
        {
          code: 'screw_holding_edge_n',
          label: 'Edge Screw Holding Load (N)',
          dataType: 'number',
          isRequired: false,
          isLocalizable: false,
          isEditable: true,
          unitSymbol: 'N',
          value: getVal('screw_holding_edge_n'),
          dataState: 'approved',
          options: [],
        },
        {
          code: 'thermal_conductivity_w_mk',
          label: 'Thermal Conductivity (k-value)',
          dataType: 'decimal',
          isRequired: false,
          isLocalizable: false,
          isEditable: true,
          unitSymbol: 'W/m·K',
          value: getVal('thermal_conductivity_w_mk'),
          dataState: 'approved',
          options: [],
        },
      ],
    },
  ];

  let requiredCount = 0;
  let requiredFilledCount = 0;

  for (const group of groups) {
    for (const attr of group.attributes) {
      if (attr.isRequired) {
        requiredCount++;
        if (attr.value !== null && attr.value !== '') {
          requiredFilledCount++;
        }
      }
    }
  }

  const completenessPct =
    requiredCount > 0 ? Math.round((requiredFilledCount / requiredCount) * 100) : 100;

  return {
    productCode: code,
    baseLocale: 'en-GB',
    completenessPct,
    requiredCount,
    requiredFilledCount,
    rowVersion: 1,
    groups,
  };
}

/**
 * Merges backend live attributes with the complete 24-attribute Excel template.
 * Guarantees that all 7 groups and 24 attributes from the Excel sheet are always visible,
 * while respecting any live database values.
 */
export function mergeWithExcelAttributes(
  code: string,
  live?: ProductAttributes | null,
): ProductAttributes {
  const template = buildExcelPlywoodAttributes(code);
  if (!live || !live.groups || live.groups.length === 0) {
    return template;
  }

  const liveValues: Record<string, string> = {};
  for (const group of live.groups) {
    for (const attr of group.attributes) {
      if (attr.value !== null && attr.value !== undefined) {
        liveValues[attr.code] = attr.value;
      }
    }
  }

  for (const group of template.groups) {
    for (const attr of group.attributes) {
      if (liveValues[attr.code] !== undefined) {
        attr.value = liveValues[attr.code];
      }
    }
  }

  let requiredCount = 0;
  let requiredFilledCount = 0;
  for (const group of template.groups) {
    for (const attr of group.attributes) {
      if (attr.isRequired) {
        requiredCount++;
        if (attr.value !== null && attr.value !== '') {
          requiredFilledCount++;
        }
      }
    }
  }

  template.requiredCount = requiredCount;
  template.requiredFilledCount = requiredFilledCount;
  template.completenessPct =
    requiredCount > 0 ? Math.round((requiredFilledCount / requiredCount) * 100) : 100;
  template.rowVersion = live.rowVersion || template.rowVersion;

  return template;
}

/**
 * Builds the complete Planning & BIM model synced directly from the product's
 * Excel attributes and BIM readiness specification.
 */
export function buildExcelPlywoodPlanning(
  code: string,
  live?: ProductPlanning | null,
): ProductPlanning {
  const normCode = code.toUpperCase();
  const attrs = buildExcelPlywoodAttributes(code);

  const getAttrVal = (attrCode: string): string | null => {
    for (const g of attrs.groups) {
      const match = g.attributes.find((a) => a.code === attrCode);
      if (match) return match.value;
    }
    return null;
  };

  const bimReadyRaw = getAttrVal('bim_ready');
  const bimReady = bimReadyRaw === 'true' ? true : bimReadyRaw === 'false' ? false : null;

  const parameters: PlanningParameter[] = [
    {
      code: 'bim_ready',
      label: 'BIM object available',
      value: bimReadyRaw,
      unitSymbol: null,
      dataType: 'boolean',
    },
    {
      code: 'mfg_length_mm',
      label: 'Manufacturing length',
      value: getAttrVal('mfg_length_mm'),
      unitSymbol: 'mm',
      dataType: 'measure',
    },
    {
      code: 'mfg_width_mm',
      label: 'Manufacturing width',
      value: getAttrVal('mfg_width_mm'),
      unitSymbol: 'mm',
      dataType: 'measure',
    },
    {
      code: 'mfg_thickness_mm',
      label: 'Calibrated Panel Thickness',
      value: getAttrVal('mfg_thickness_mm'),
      unitSymbol: 'mm',
      dataType: 'measure',
    },
    {
      code: 'modulus_elasticity_mpa',
      label: 'Modulus of Elasticity (MOE)',
      value: getAttrVal('modulus_elasticity_mpa'),
      unitSymbol: 'MPa',
      dataType: 'measure',
    },
    {
      code: 'modulus_rupture_mpa',
      label: 'Modulus of Rupture (MOR)',
      value: getAttrVal('modulus_rupture_mpa'),
      unitSymbol: 'MPa',
      dataType: 'measure',
    },
    {
      code: 'screw_holding_face_n',
      label: 'Face Screw Holding Load',
      value: getAttrVal('screw_holding_face_n'),
      unitSymbol: 'N',
      dataType: 'number',
    },
    {
      code: 'screw_holding_edge_n',
      label: 'Edge Screw Holding Load',
      value: getAttrVal('screw_holding_edge_n'),
      unitSymbol: 'N',
      dataType: 'number',
    },
    {
      code: 'thermal_conductivity_w_mk',
      label: 'Thermal Conductivity (k-value)',
      value: getAttrVal('thermal_conductivity_w_mk'),
      unitSymbol: 'W/m·K',
      dataType: 'decimal',
    },
  ];

  const bimReferences: BimReference[] =
    live?.bimReferences && live.bimReferences.length > 0
      ? live.bimReferences
      : [
          {
            schemeCode: 'ETIM',
            schemeName: 'ETIM 9.0',
            schemeVersion: '9.0',
            classCode: 'EC002620',
            classLabel: 'Plywood',
          },
          {
            schemeCode: 'IFC',
            schemeName: 'IFC 4.3 BuildingSMART',
            schemeVersion: '4.3.0.0',
            classCode: 'IfcPlate.SHEET',
            classLabel: 'Sheet / Plate panel element',
          },
          {
            schemeCode: 'OMNICLASS',
            schemeName: 'OmniClass Construction Classification',
            schemeVersion: '2020',
            classCode: '23-13 23 13',
            classLabel: 'Plywood and Composite Panels',
          },
          {
            schemeCode: 'UNICLASS',
            schemeName: 'Uniclass 2015',
            schemeVersion: 'v1.28',
            classCode: 'Pr_25_71_92_64',
            classLabel: 'Plywood sheets',
          },
        ];

  const geometryFiles: GeometryFile[] =
    live?.geometryFiles && live.geometryFiles.length > 0
      ? live.geometryFiles
      : bimReady
        ? [
            {
              linkId: 501,
              role: 'BIM_MODEL',
              assetType: 'BIM',
              fileName: `${normCode.toLowerCase()}_revit_bim.ifc`,
              fileSize: 4850000,
              url: `/assets/bim/${normCode.toLowerCase()}.ifc`,
              title: '3D BIM Component (IFC 4.3)',
              isExternal: true,
            },
            {
              linkId: 502,
              role: 'CAD_DRAWING',
              assetType: 'CAD',
              fileName: `${normCode.toLowerCase()}_section_detail.dwg`,
              fileSize: 1250000,
              url: `/assets/cad/${normCode.toLowerCase()}.dwg`,
              title: '2D Architectural Joint Details (DWG)',
              isExternal: true,
            },
          ]
        : [];

  const parameterCount = parameters.length;
  const parameterFilledCount = parameters.filter((p) => p.value !== null && p.value !== '').length;

  return {
    productCode: code,
    rowVersion: live?.rowVersion ?? 1,
    bimReady,
    parameterCount,
    parameterFilledCount,
    parameters,
    bimReferences,
    geometryFiles,
  };
}


