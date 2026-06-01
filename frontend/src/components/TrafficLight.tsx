type Props = { color: 'green' | 'yellow' | 'red' | 'gray' }

const colors = {
  green:  'bg-green-500',
  yellow: 'bg-yellow-400',
  red:    'bg-red-500',
  gray:   'bg-gray-300',
}

export default function TrafficLight({ color }: Props) {
  return (
    <span className={`inline-block w-3 h-3 rounded-full ${colors[color]}`} />
  )
}