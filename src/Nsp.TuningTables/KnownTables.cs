namespace Nsp.TuningTables;

public static class KnownTables
{
    public static TableDefinition IgnitionDwellTime { get; } = new()
    {
        TableId = "ignition_dwell_time", DisplayName = "Ignition Dwell Time",
        RowAxisLabel = "None (one-dimensional table)", ColumnAxisLabel = "Device Battery Voltage (Volts)",
        CellUnit = "ms", DriverKind = TableDriverKind.Uia, NavigationSearchText = "Dwell",
        HelpIdentity = "Engine Configuration - Ignition System - Dwell Time", CellDecimalPlaces = 3,
    };

    public static TableDefinition InjectorStage1DeadTime { get; } = new()
    {
        TableId = "injector_stage1_dead_time", DisplayName = "Injection Stage 1 Dead Time",
        RowAxisLabel = "None (one-dimensional table)", ColumnAxisLabel = "Device Battery Voltage (Volts)",
        CellUnit = "ms", DriverKind = TableDriverKind.Uia, NavigationSearchText = "Dead Time",
        SupportsColumnBreakpointEdits = true,
        HelpIdentity = "Fuel Tuning - Stage 1 - Dead Time", CellDecimalPlaces = 3,
    };

    public static TableDefinition BaseFuel { get; } = new()
    {
        TableId = "base_fuel", DisplayName = "Base Fuel Tuning",
        RowAxisLabel = "RPM (RPM)", ColumnAxisLabel = "Fuel - Load (MAP) (psi/inHg)",
        CellUnit = "%", DriverKind = TableDriverKind.Uia, NavigationSearchText = "Base",
        HelpIdentity = "Fuel Tuning - Base", CellDecimalPlaces = 1, ReadDescendingRowsFromAxisSetup = true, SupportsSliceAxis = true,
    };

    public static TableDefinition BaseIgnition { get; } = new()
    {
        TableId = "base_ignition", DisplayName = "Base Ignition Angle",
        RowAxisLabel = "RPM (RPM)", ColumnAxisLabel = "Ignition - Load (MAP) (psi/inHg)",
        CellUnit = "degrees BTDC", DriverKind = TableDriverKind.Uia, NavigationSearchText = "Base",
        HelpIdentity = "Ignition Tuning - Base", CellDecimalPlaces = 1, ReadDescendingRowsFromAxisSetup = true, SupportsSliceAxis = true,
    };

    public static TableDefinition CoolantTemperatureBias { get; } = new()
    {
        TableId = "coolant_temperature_bias", DisplayName = "Calculated Air Temperature Coolant Temperature Bias",
        RowAxisLabel = "RPM (RPM)", ColumnAxisLabel = "Manifold Pressure (psi/inHg)",
        CellUnit = "%", DriverKind = TableDriverKind.Uia, NavigationSearchText = "Coolant Temp Bias",
        SupportsRowBreakpointEdits = true,
        HelpIdentity = "Sensors - Coolant Temp Bias", CellDecimalPlaces = 1, ReadDescendingRowsFromAxisSetup = true,
    };

    public static TableDefinition TargetLambdaCoolantCorrection { get; } = new()
    {
        TableId = "target_lambda_coolant_correction", DisplayName = "Target Lambda Coolant Correction",
        RowAxisLabel = "Manifold Pressure (psi/inHg)", ColumnAxisLabel = "Coolant Temperature (°F)",
        CellUnit = "AFR-p offset (current display)", DriverKind = TableDriverKind.Uia, NavigationSearchText = "Coolant Temp Corr",
        HelpIdentity = "Fuel Tuning - Lambda Corrections - Coolant Temp Corr", CellDecimalPlaces = 1, ReadDescendingRowsFromAxisSetup = true,
    };

    public static TableDefinition CrankingFuel { get; } = new()
    {
        TableId = "cranking_fuel", DisplayName = "Fuel Cranking",
        RowAxisLabel = "None (one-dimensional table)", ColumnAxisLabel = "Coolant Temperature (°F)",
        CellUnit = "NSP fuel amount (current display)", DriverKind = TableDriverKind.Uia, NavigationSearchText = "Cranking",
        HelpIdentity = "Fuel Tuning - Cranking", CellDecimalPlaces = 1,
    };

    public static TableDefinition CrankingIgnition { get; } = new()
    {
        TableId = "cranking_ignition", DisplayName = "Cranking Ignition Angle",
        RowAxisLabel = "None (one-dimensional table)", ColumnAxisLabel = "Coolant Temperature (°F)",
        CellUnit = "degrees BTDC", DriverKind = TableDriverKind.Uia, NavigationSearchText = "Cranking",
        SupportsColumnBreakpointEdits = true,
        HelpIdentity = "Ignition Tuning - Cranking", CellDecimalPlaces = 1,
    };

    public static TableDefinition TargetLambda { get; } = new()
    {
        TableId = "target_lambda", DisplayName = "Target Lambda Table Output",
        RowAxisLabel = "RPM (RPM)", ColumnAxisLabel = "Manifold Pressure (psi/inHg)",
        CellUnit = "AFR-p (current display)", DriverKind = TableDriverKind.Uia, NavigationSearchText = "Target Lambda",
        HelpIdentity = "Fuel Tuning - Target Lambda", CellDecimalPlaces = 1, ReadDescendingRowsFromAxisSetup = true, SupportsSliceAxis = true,
    };

    public static TableDefinition TargetLambdaAirCorrection { get; } = new()
    {
        TableId = "target_lambda_air_correction", DisplayName = "Target Lambda Air Temperature Correction",
        RowAxisLabel = "Manifold Pressure (psi/inHg)", ColumnAxisLabel = "Intake Air Temperature (°F)",
        CellUnit = "AFR-p offset (current display)", DriverKind = TableDriverKind.Uia, NavigationSearchText = "Air Temp Corr",
        HelpIdentity = "Fuel Tuning - Lambda Corrections - Air Temp Corr", CellDecimalPlaces = 1, ReadDescendingRowsFromAxisSetup = true,
    };

    public static TableDefinition IgnitionCoolantCorrection { get; } = new()
    {
        TableId = "ignition_coolant_correction", DisplayName = "Coolant Temperature Ignition Correction",
        RowAxisLabel = "Coolant Temperature (°F)", ColumnAxisLabel = "Manifold Pressure (psi/inHg)",
        CellUnit = "degrees correction", DriverKind = TableDriverKind.Uia, NavigationSearchText = "Coolant Temp Correction",
        HelpIdentity = "Ignition Tuning - Corrections - Coolant Temp Correction", CellDecimalPlaces = 1, ReadDescendingRowsFromAxisSetup = true,
    };

    public static TableDefinition IgnitionAirCorrection { get; } = new()
    {
        TableId = "ignition_air_correction", DisplayName = "Air Temperature Ignition Correction",
        RowAxisLabel = "Intake Air Temperature (°F)", ColumnAxisLabel = "Manifold Pressure (psi/inHg)",
        CellUnit = "degrees correction", DriverKind = TableDriverKind.Uia, NavigationSearchText = "Air Temp Correction",
        HelpIdentity = "Ignition Tuning - Corrections - Air Temp Correction", CellDecimalPlaces = 1, ReadDescendingRowsFromAxisSetup = true,
    };

    public static TableDefinition FuelPostStartCorrection { get; } = new()
    {
        TableId = "fuel_post_start_correction", DisplayName = "Fuel Post Start Correction",
        RowAxisLabel = "Coolant Temperature (°F)", ColumnAxisLabel = "Engine Running Time (s)",
        CellUnit = "%", DriverKind = TableDriverKind.Uia, NavigationSearchText = "Post Start",
        HelpIdentity = "Fuel Tuning - Corrections - Post Start Correction", CellDecimalPlaces = 1, ReadDescendingRowsFromAxisSetup = true,
    };

    public static TableDefinition BoostClosedLoopDuty { get; } = new()
    {
        TableId = "boost_closed_loop_duty", DisplayName = "Boost Control Closed Loop Base Output",
        RowAxisLabel = "RPM (RPM)", ColumnAxisLabel = "BoostTargPrs(Corr) (psi/inHg)",
        CellUnit = "%", DriverKind = TableDriverKind.Uia, NavigationSearchText = "Closed Loop Base Duty",
        HelpIdentity = "Engine Functions - Boost Control - Closed Loop Base Duty Cycle", CellDecimalPlaces = 1, ReadDescendingRowsFromAxisSetup = true, SupportsSliceAxis = true,
    };

    public static TableDefinition BoostTargetPressure { get; } = new()
    {
        TableId = "boost_target_pressure", DisplayName = "Boost Control Target Pressure",
        RowAxisLabel = "None (one-dimensional table)", ColumnAxisLabel = "RPM (RPM)",
        CellUnit = "psi (current display)", DriverKind = TableDriverKind.Uia, NavigationSearchText = "Closed Loop Target Pressure",
        HelpIdentity = "Engine Functions - Boost Control - Closed Loop Target Pressure", CellDecimalPlaces = 1, SupportsSliceAxis = true, ReadDescendingRowsFromAxisSetup = true,
    };

    public static TableDefinition InjectorStage1FlowRate { get; } = new()
    {
        TableId = "injector_stage1_flow_rate", DisplayName = "Injection Stage 1 Flow Rate",
        RowAxisLabel = "None (scalar)", ColumnAxisLabel = "None (scalar)", HasNoAxes = true,
        CellUnit = "lb/hr (current display)", DriverKind = TableDriverKind.Uia, NavigationSearchText = "Flow Rate",
        HelpIdentity = "Fuel Tuning - Stage 1 - Flow Rate", CellDecimalPlaces = 2,
    };

    public static IReadOnlyList<TableDefinition> All { get; } =
        [IgnitionDwellTime, InjectorStage1DeadTime, BaseFuel, BaseIgnition, CoolantTemperatureBias, TargetLambdaCoolantCorrection, CrankingFuel, CrankingIgnition,
         TargetLambda, TargetLambdaAirCorrection, IgnitionCoolantCorrection, IgnitionAirCorrection, FuelPostStartCorrection,
         BoostClosedLoopDuty, BoostTargetPressure, InjectorStage1FlowRate];
}
