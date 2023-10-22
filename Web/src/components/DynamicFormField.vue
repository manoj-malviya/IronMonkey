<script setup>
import { computed, useSlots } from 'vue'

defineProps({
  label: {
    type: String,
    default: null
  },
  labelFor: {
    type: String,
    default: null
  },
  help: {
    type: String,
    default: null
  }
})

const slots = useSlots()

// const hasActionsSlot = computed(() => slots.actions && !!slots.actions())

const wrapperClass = computed(() => {
  const base = []
  const slotsLength = slots.default().length

  if (slotsLength > 1) {
    base.push('grid grid-cols-1 gap-2')
  }

  if (slotsLength === 2) {
    base.push('md:grid-cols-3')
  }

  if (slotsLength === 3) {
    base.push('md:grid-cols-3')
  }

  return base
})
</script>

<template>
  <div class="mb-2 last:mb-0">
    <label v-if="label" :for="labelFor" class="block mb-1">{{ label }}</label>
      <div :class="wrapperClass">
        <slot />
        <div class="col-span-1">
          <slot name="actions" />
        </div>
      </div>
    <div v-if="help" class="text-xs text-gray-500 dark:text-slate-400 mt-1">
      {{ help }}
    </div>

  </div>
</template>
