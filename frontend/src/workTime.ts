import type { CostLine } from './types'

export const isTimedLine = (line: CostLine) => line.unit === 'hod' && (line.category === 'labor' || line.category === 'service' && line.serviceCategory === 'machinery')

export const timeParts = (quantity: number) => {
  const minutes = Math.round(quantity * 60)
  return { hours: Math.floor(minutes / 60), minutes: minutes % 60 }
}

export const formatTime = (quantity: number) => {
  const { hours, minutes } = timeParts(quantity)
  return `${hours} h ${String(minutes).padStart(2, '0')} min`
}

export const roundedTimeQuantities = (lines: CostLine[]) => {
  const quantities = lines.map(line => line.quantity)
  const groups = new Map<string, number[]>()
  lines.forEach((line, index) => {
    if (!isTimedLine(line)) return
    const key = JSON.stringify([line.workDate || '', line.category, line.name, line.unitPrice, line.vatRate, line.automaticMachineryCharge || ''])
    groups.set(key, [...(groups.get(key) || []), index])
  })
  for (const indices of groups.values()) {
    const total = indices.reduce((sum, index) => sum + lines[index].quantity, 0)
    quantities[indices[indices.length - 1]] += Math.ceil(total * 4 - 1e-9) / 4 - total
  }
  return quantities
}
