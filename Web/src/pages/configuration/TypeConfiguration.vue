<script setup>
import { reactive, ref } from 'vue'
import { mdiBallotOutline, mdiAccount, mdiMail, mdiGithub } from '@mdi/js'
import SectionMain from '@/components/SectionMain.vue'
import CardBox from '@/components/CardBox.vue'
import FormCheckRadioGroup from '@/components/FormCheckRadioGroup.vue'
import FormField from '@/components/FormField.vue'
import FormControl from '@/components/FormControl.vue'
import BaseDivider from '@/components/BaseDivider.vue'
import BaseButton from '@/components/BaseButton.vue'
import BaseButtons from '@/components/BaseButtons.vue'
import SectionTitle from '@/components/SectionTitle.vue'
import LayoutAuthenticated from '@/layouts/LayoutAuthenticated.vue'
import SectionTitleLineWithButton from '@/components/SectionTitleLineWithButton.vue'
import NotificationBarInCard from '@/components/NotificationBarInCard.vue'

const selectOptions = [
  { id: 1, label: 'Business development' },
  { id: 2, label: 'Marketing' },
  { id: 3, label: 'Sales' }
];

const typeOptions = [
  {id:1, label: 'String'},
  {id:2, label: 'Integer'},
  {id:3, label: 'Double'},
  {id:4, label: 'Boolean'}
];

const statusOptions = [
  {id: 0, label: 'Draft'},
  {id: 1, label: 'Active'},
  {id:2, label: 'InActive'}
];

const validationOptions = [
  {name: 'Required', options:{}},
  {name: 'Minimum', options:{}}
]


const field = {
  name: '',
  type: 0
};

const form = reactive({
  name: '',
  storage: '',
  desc: '',
  department: 0,
  fields: [field],
  status: 0
});

const addField = () => {
  form.fields.push({ ...field });
};

const submit = () => {
  //
}

const formStatusCurrent = ref(0)

const formStatusOptions = ['info', 'success', 'danger', 'warning']

const formStatusSubmit = () => {
  formStatusCurrent.value = formStatusOptions[formStatusCurrent.value + 1]
    ? formStatusCurrent.value + 1
    : 0
}
</script>

<template>
  <LayoutAuthenticated>
    <SectionMain>
      <SectionTitleLineWithButton :icon="mdiBallotOutline" title="Define your Type" main />
      <CardBox form @submit.prevent="submit">
        <FormField label="Type Basic">
          <FormControl v-model="form.name" :icon="mdiAccount" />
          <FormControl v-model="form.storage" :icon="mdiMail" />
        </FormField>

        <FormField label="Description">
          <FormControl v-model="form.desc" placeholder="Description About this Type" />
        </FormField>

        <FormField label="Status">
          <FormControl v-model="form.status" :options="statusOptions" />
        </FormField>

        <BaseDivider />

        <FormField label="Field" v-for="(field, index) in form.fields" :key="index">
            <FormControl v-model="field.name" :icon="mdiAccount" />
            <FormControl v-model="field.type" :options="typeOptions" />
          <BaseButton @click="addField" color="info" label="Add Field" />
        </FormField>

        <template #footer>
          <BaseButtons>
            <BaseButton type="submit" color="info" label="Submit" />
            <BaseButton type="reset" color="info" outline label="Reset" />
          </BaseButtons>
        </template>
      </CardBox>
    </SectionMain>
  </LayoutAuthenticated>
</template>
