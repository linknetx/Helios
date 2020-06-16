function driver.processExports(selfData)
	local altBar = LoGetAltitudeAboveSeaLevel()
	local altRad = LoGetAltitudeAboveGroundLevel()
	local pitch, bank, yaw = LoGetADIPitchBankYaw()
	local engine = LoGetEngineInfo()
	local hsi    = LoGetControlPanel_HSI()
	local vvi = LoGetVerticalVelocity()
	local ias = LoGetIndicatedAirSpeed()
	local route = LoGetRoute()
	local aoa = LoGetAngleOfAttack()
	local distanceToWay = 999
	
	local glide = LoGetGlideDeviation()
	local side = LoGetSideDeviation()

	if (selfData ~= nil) then
		local myLoc = LoGeoCoordinatesToLoCoordinates(selfData.LatLongAlt.Long, selfData.LatLongAlt.Lat)
		distanceToWay = math.sqrt((myLoc.x - route.goto_point.world_point.x)^2 + (myLoc.y -  route.goto_point.world_point.y)^2)
	else
		-- XXX log only once
		log.write("HELIOS.EXPORT", log.DEBUG, "FC2 export received nothing from LoGetSelfData; cannot send location")
	end

	if (pitch ~= nill) then
		helios.send(1, pitch * 57.3, "%.2f")
		helios.send(2, bank * 57.3, "%.2f")
		helios.send(3, yaw * 57.3, "%.2f")
		helios.send(4, altBar, "%.2f")
		helios.send(5, altRad, "%.2f")
		helios.send(6, 360 - ((hsi.ADF or hsi.ADF_raw or 0.0) * 57.3), "%.2f")
		helios.send(7, 360 - ((hsi.RMI or hsi.RMI_raw or 0.0) * 57.3), "%.2f")
		helios.send(8, ((hsi.Compass or selfData.Heading or 0.0) * 57.3), "%.2f")
		helios.send(9, engine.RPM.left, "%.2f")
		helios.send(10, engine.RPM.right, "%.2f")
		helios.send(11, engine.Temperature.left, "%.2f")
		helios.send(12, engine.Temperature.right, "%.2f")
		helios.send(13, vvi, "%.2f")
		helios.send(14, ias, "%.2f")
		helios.send(15, distanceToWay, "%.2f")
		helios.send(16, aoa, "%.2f")
		helios.send(17, glide, "%.2f")
		helios.send(18, side, "%.2f")
	else
		-- XXX log only once
		log.write("HELIOS.EXPORT", log.DEBUG, "FC2 export received nothing from LoGetADIPitchBankYaw; cannot send telemetry")
	end
end